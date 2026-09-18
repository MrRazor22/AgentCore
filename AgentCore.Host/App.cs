using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.Host.Abstractions;
using AgentCore.Host.Ipc;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Tornado;
using AgentCore.Layers.Context;
using AgentCore.Layers.LLM;
using AgentCore.Layers.Tools;
using CodeSharp.Skills;
using CodeSharp.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace AgentCore.Host;

internal class App
{
    private sealed class Config
    {
        public string BaseUrl { get; set; } = "http://127.0.0.1:1234/v1";
        public string Model { get; set; } = "model";
        public string ApiKey { get; set; } = "lmstudio";
    }

    public static async Task Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        string root = FindRoot();
        var config = LoadConfig(root);

        using var lf = LoggerFactory.Create(b => b
            .AddConsole(c => c.LogToStandardErrorThreshold = LogLevel.Trace)
            .SetMinimumLevel(LogLevel.Information));

        await using IIpcChannel channel = new StdioIpcChannel();
        IVsToolDispatcher dispatcher = new VsToolDispatcher(channel);
        var vsTools = new VsStudioTools(dispatcher);
        var skillTool = new SkillTool(new SkillManager(root));
        var webTools = new WebTools();
        var scheduleTool = new ScheduleTool();

        var baseUrl = config.BaseUrl.EndsWith('/') ? config.BaseUrl : config.BaseUrl + "/";
        var agent = Agent.Create()
            .WithLoggerFactory(lf)
            .WithTornado(config.ApiKey, config.Model, baseUrl)
            .UseContext(ctx => ctx
                .WithChatContext(contextWindow: 50000, reserveTokens: 2500)
                .AddChatPersistence(Path.Combine(root, ".codesharp", "sessions"), Guid.NewGuid().ToString(), enableWal: true)
                .AddChatGrammar())
            .UseLLM(llm => llm.WithRetry().WithToolCallDetection())
            .UseTool(tools => tools
                .WithTools(vsTools)
                .WithTools(skillTool)
                .WithToolDiscovery()
                .WithTools(webTools, new Discoverable("web"))
                .WithTools(scheduleTool, new Discoverable("schedule")))
            .WithInstructions([new Text("You are Devin Agent embedded in Visual Studio. Keep responses precise. Prefer ReadFile, EditFile, Search.")])
            .Build();

        await channel.SendAsync(new("ready", Name: config.Model));

        await foreach (var msg in channel.ReadMessagesAsync())
        {
            if (msg.Type == "tool_result" && msg.Id != null)
            {
                dispatcher.TryHandleResult(msg.Id, msg.Result, msg.Error);
                continue;
            }

            if (msg.Type == "prompt" && !string.IsNullOrWhiteSpace(msg.Text))
            {
                _ = ExecutePromptAsync(agent, channel, msg.Text);
            }
        }
    }

    private static async Task ExecutePromptAsync(IAgent agent, IIpcChannel channel, string prompt)
    {
        try
        {
            var input = new IContent[] { new Text(prompt) };
            await foreach (var evt in agent.InvokeStreamingAsync(input))
            {
                IpcMessage? payload = evt switch
                {
                    TextDelta td => new("content", Text: td.Text),
                    Text t => new("content", Text: t.Value),
                    ReasoningDelta rd => new("thought", Text: rd.Thought),
                    Reasoning r => new("thought", Text: r.Thought),
                    ToolCallStart tcs => new("tool_start", Id: tcs.Id, Name: tcs.Name),
                    ToolCall tc => new("tool_call_ui", Id: tc.Id, Name: tc.Name, Text: tc.Arguments),
                    _ => null
                };
                if (payload != null) await channel.SendAsync(payload);
            }
            await channel.SendAsync(new("done"));
        }
        catch (Exception ex)
        {
            await channel.SendAsync(new("error", Text: ex.Message));
        }
    }

    private static Config LoadConfig(string root)
    {
        string[] paths = [Path.Combine(root, "config.json"), Path.Combine(root, "AgentCore.Host", "config.json"), Path.Combine(AppContext.BaseDirectory, "config.json")];
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(p), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch { }
        }
        return new Config();
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AgentCore.sln")) || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        return Directory.GetCurrentDirectory();
    }
}
