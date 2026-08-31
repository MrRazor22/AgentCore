using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.LLM;
using AgentCore.Tools;
using AgentCore.Context;
using Spectre.Console;
using CodeSharp.UI;
using AgentCore.Layers.LLM;
using Serilog;
using Microsoft.Extensions.Logging;

namespace CodeSharp;

internal class App
{
    private class Config
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    private static async Task Main(string[] args)
    {
        // Set console output encoding to UTF8 for proper rendering of unicode symbols
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 1. Locate config.json
        string configPath = "config.json";
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        }
        if (!File.Exists(configPath))
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "config.json");
                if (File.Exists(candidate))
                {
                    configPath = candidate;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine("[bold red]Error:[/] Could not find [yellow]config.json[/] in any parent directories.");
            return;
        }

        // 2. Read config
        Config? config;
        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            config = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error reading config.json:[/] {ex.Message}");
            return;
        }

        if (config == null || string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Model))
        {
            AnsiConsole.MarkupLine("[bold red]Error:[/] Invalid configuration in [yellow]config.json[/]. Please check BaseUrl and Model.");
            return;
        }

        // Determine actual workspace path (upwards traversal to find .git or .sln)
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

        // 3. Build Agent and Run UI
        try
        {
            // 3. Configure Serilog rolling file logger
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "agentcore-.log"),
                    rollingInterval: Serilog.RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            var lf = new Microsoft.Extensions.Logging.LoggerFactory()
                .AddSerilog();

            var baseUrl = config.BaseUrl;
            if (!string.IsNullOrWhiteSpace(baseUrl) && !baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }

            var streamingLayer = new StreamingEventLayer<object>();

            // Universal PowerShell execution tool with workspace boundary enforcement
            var shellTool = new CodeSharp.Tools.ShellTool(workspacePath);

            var formatter = new CodeSharp.UI.CompositeToolDisplayFormatter(new CodeSharp.UI.IToolDisplayFormatter[]
            {
                new CodeSharp.UI.RunCommandFormatter()
            });

            var prompt = new CodeSharp.UI.ConsoleApprovalPrompt(formatter);

            var approvalLayer = new ToolApprovalLayer(async (call, ct) =>
            {
                return await prompt.RequestApprovalAsync(call, ct).ConfigureAwait(false)
                    ? null
                    : (AgentCore.LLM.Chat.IContent?)new AgentCore.LLM.Chat.Text("[DENIED] User rejected execution.");
            });

            IAgent agent = Agent.Create()
                .WithLoggerFactory(lf)
                .WithTornado(apiKey: config.ApiKey, model: config.Model, baseUrl: baseUrl)
                .WithChatContext(contextWindow: 50000, reserveTokens: 2500)
                .AddLLMLayer(new MessageCoalescingLayer())
                .AddLLMLayer(streamingLayer)
                .AddLLMLayer(new ToolCallDetectionLayer())
                .WithTools(shellTool)
                .AddToolingLayer(approvalLayer)
                .WithInstructions(
                    "You are CodeSharp, an expert agentic AI coding assistant.\n" +
                    "Keep your responses precise, direct, and to the point. Do not add needless filler, conversational bloat, or generic pleasantries.\n" +
                    "You have a single universal execution tool: RunCommand.\n" +
                    "Use PowerShell cmdlets and standard CLI utilities to inspect files, edit code, search directory structures, run builds, execute tests, and manage git repositories."
                )
                .Build();

            var chatUi = new ChatUI(agent, config.Model, workspacePath, streamingLayer, formatter);
            await chatUi.RunAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error building agent:[/] {Markup.Escape(ex.Message)}");
            Serilog.Log.Error(ex, "Error starting CodeSharp");
        }
    }
}
