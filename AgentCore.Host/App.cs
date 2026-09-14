using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Tornado;
using AgentCore.Layers.Chat;
using AgentCore.Layers.LLM;
using AgentCore.Layers.Tools;
using Serilog;
using CodeSharp.Skills;
using CodeSharp.Tools;

namespace AgentCore.Host;

public record PipeMessage(string? Type, string? Text, string? Workspace);

internal class App
{
    private class Config
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string? Provider { get; set; }
        public string? PipeName { get; set; }
    }

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 1. Parse pipe name from arguments (e.g. --pipe AgentCore_12345)
        string pipeName = "AgentCore_Host_Pipe";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pipe" && i + 1 < args.Length)
            {
                pipeName = args[i + 1];
            }
        }

        // 2. Read config.json
        string configPath = "config.json";
        if (!File.Exists(configPath)) configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "config.json");
                if (File.Exists(candidate)) { configPath = candidate; break; }
                dir = Path.GetDirectoryName(dir);
            }
        }

        Config config = new();
        if (File.Exists(configPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(configPath);
                config = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl)) config.BaseUrl = "http://127.0.0.1:1234/v1";
        if (string.IsNullOrWhiteSpace(config.Model)) config.Model = "model";
        if (string.IsNullOrWhiteSpace(config.ApiKey)) config.ApiKey = "lmstudio";

        // Determine workspace path
        string workspacePath = Directory.GetCurrentDirectory();
        var searchDir = AppContext.BaseDirectory;
        while (searchDir != null)
        {
            if (Directory.Exists(Path.Combine(searchDir, ".git")) || File.Exists(Path.Combine(searchDir, "AgentCore.sln")))
            {
                workspacePath = searchDir;
                break;
            }
            searchDir = Path.GetDirectoryName(searchDir);
        }

        // Configure logger
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDir, "agentcore-host-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var lf = new Microsoft.Extensions.Logging.LoggerFactory().AddSerilog();

        // Build Agent
        var baseUrl = config.BaseUrl;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";

        var sessionsDir = Path.Combine(workspacePath, ".codesharp", "sessions");
        var spilloverDir = Path.Combine(workspacePath, ".codesharp", "spillover");

        var shellTool = new ShellTool(workspacePath, spilloverDir: spilloverDir);
        var skillManager = new SkillManager(workspacePath);
        var skillTool = new SkillTool(skillManager);

        var agent = Agent.Create()
            .WithLoggerFactory(lf)
            .WithTornado(config.ApiKey, config.Model, baseUrl)
            .UseContext(ctx => ctx
                .WithChatContext(contextWindow: 50000, reserveTokens: 2500)
                .AddChatPersistence(sessionsDir, Guid.NewGuid().ToString(), enableWal: true))
            .UseLLM(llm => llm
                .WithRetry()
                .WithToolCallDetection()
                .WithMessageCoalescing())
            .UseToolbox(tools => tools.WithTools(shellTool).WithTools(skillTool))
            .WithInstructions(
                """
                You are Devin Agent, an expert AI coding assistant embedded in Visual Studio.
                Keep your responses precise, direct, and to the point.
                You have tools: RunCommand, ViewSkill. Use standard PowerShell cmdlets and standard CLI utilities to inspect files, edit code, search directory structures, run builds, execute tests, and manage git repositories.
                """)
            .Build();

        Console.WriteLine($"[AgentCore.Host] Starting Named Pipe Server: {pipeName}");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        // Named pipe listening loop
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                Console.WriteLine($"[AgentCore.Host] Waiting for Visual Studio connection on '{pipeName}'...");
                await server.WaitForConnectionAsync(cts.Token);
                Console.WriteLine("[AgentCore.Host] Visual Studio client connected.");

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                // Send initial ready handshake
                var readyMsg = JsonSerializer.Serialize(new { @event = "ready", model = config.Model, provider = config.Provider ?? "Tornado" });
                await writer.WriteLineAsync(readyMsg);

                while (!cts.IsCancellationRequested && server.IsConnected)
                {
                    string? line = await reader.ReadLineAsync();
                    if (line == null) break; // Client disconnected

                    PipeMessage? msg = null;
                    try
                    {
                        msg = JsonSerializer.Deserialize<PipeMessage>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AgentCore.Host] JSON parse error: {ex.Message}");
                    }

                    if (msg == null || string.IsNullOrWhiteSpace(msg.Text)) continue;

                    Console.WriteLine($"[AgentCore.Host] Prompt received: {msg.Text}");

                    // Stream execution to pipe
                    try
                    {
                        var input = new IContent[] { new Text(msg.Text) };
                        await foreach (var evt in agent.InvokeStreamingAsync(input, ct: cts.Token))
                        {
                            string eventType = "unknown";
                            object? eventData = null;

                            switch (evt)
                            {
                                case TextDelta td:
                                    eventType = "content";
                                    eventData = new { delta = td.Text };
                                    break;
                                case Text t:
                                    eventType = "content";
                                    eventData = new { delta = t.Value };
                                    break;
                                case ReasoningDelta rd:
                                    eventType = "thought";
                                    eventData = new { thought = rd.Thought };
                                    break;
                                case Reasoning r:
                                    eventType = "thought";
                                    eventData = new { thought = r.Value };
                                    break;
                                case ToolCallStart tcs:
                                    eventType = "tool_start";
                                    eventData = new { id = tcs.Id, name = tcs.Name };
                                    break;
                                case ToolCallDelta tcd:
                                    eventType = "tool_delta";
                                    eventData = new { arguments = tcd.Arguments };
                                    break;
                                case ToolCall tc:
                                    eventType = "tool_call";
                                    eventData = new { id = tc.Id, name = tc.Name, arguments = tc.Arguments };
                                    break;
                            }

                            if (eventData != null)
                            {
                                var jsonLine = JsonSerializer.Serialize(new { @event = eventType, data = eventData });
                                await writer.WriteLineAsync(jsonLine);
                            }
                        }

                        // Send done signal
                        var doneJson = JsonSerializer.Serialize(new { @event = "done" });
                        await writer.WriteLineAsync(doneJson);
                    }
                    catch (Exception ex)
                    {
                        var errJson = JsonSerializer.Serialize(new { @event = "error", message = ex.Message });
                        await writer.WriteLineAsync(errJson);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AgentCore.Host] Pipe error: {ex.Message}");
                await Task.Delay(1000, cts.Token);
            }
        }
    }
}
