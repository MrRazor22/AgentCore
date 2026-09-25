using System.Text;
using System.Text.Json;
using AgentCore;
using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.Host.Abstractions;
using AgentCore.Host.Ipc;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Tornado;
using AgentCore.Layers.Context;
using AgentCore.Layers.LLM;
using AgentCore.Layers.Tools;
using AgentCore.Tool;
using AgentCore.Tool.Tools;
using CodeSharp.Skills;
using CodeSharp.Tools; 
using LlmTornado.Chat.Models;

namespace AgentCore.Host;

internal class App
{
    private sealed record Config(string BaseUrl = "http://127.0.0.1:1234/v1", string Model = "model", string ApiKey = "lmstudio");

    public static async Task Main(string[] args)
    {
        Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;
        string root = FindRoot();
        var config = LoadConfig(root);

        await using IIpcChannel channel = new StdioIpcChannel();
        IVsToolDispatcher dispatcher = new VsToolDispatcher(channel);
        string sessionsDir = Path.Combine(root, ".codesharp", "sessions");
        string currentSessionId = Guid.NewGuid().ToString();
        var tokenizer = new Tokenizer(charsPerToken: 3);
        CancellationTokenSource activeCts = new();

        var agent = new Agent(
            llm: llm => llm
                .UseTornado(config.ApiKey, config.Model, config.BaseUrl)
                .UseRetry(maxRetries: 3, backoffMultiplier: 2.0)
                .UseToolCallDetection(),
            toolbox: tools => tools
                .Configure(parallel: true, maxConcurrency: 8, timeout: TimeSpan.FromMinutes(2))
                .AddTool(new VsStudioTools(dispatcher))
                .AddTool(new SkillTool(new SkillManager(root)))
                .AddTool(new WebTools(), new Discoverable("web"))
                .AddTool(new ScheduleTool(), new Discoverable("schedule"))
                .UseToolDiscovery(),
            context: ctx => ctx
                .Configure(
                    contextWindow: 128_000, reserveTokens: 10_000, maxSingleMessageTokens: 8_000,
                    counter: tokenizer, truncator: new Truncator(tokenizer, headRatio: 0.7, notice: "\n... [truncated]"),
                    compactor: new Summarizer(new TornadoLLM(config.ApiKey, config.Model, config.BaseUrl)))
                .UseSession(sessionsDir, currentSessionId),
            instructions: [new Text("You are Devin Agent embedded in Visual Studio. Keep responses precise. Prefer ReadFile, EditFile, Search.")]);

        await channel.SendAsync(new("ready", Name: config.Model));

        await foreach (var msg in channel.ReadMessagesAsync())
        {
            if (msg.Type == "tool_result" && msg.Id != null)
            {
                dispatcher.TryHandleResult(msg.Id, msg.Result, msg.Error);
                continue;
            }

            switch (msg.Type)
            {
                case "switch_session" when !string.IsNullOrWhiteSpace(msg.Text):
                    agent.Context.UseSession(sessionsDir, currentSessionId = msg.Text);
                    break;
                case "fork_session" when !string.IsNullOrWhiteSpace(msg.Text):
                    await agent.Context.ForkSessionAsync(sessionsDir, currentSessionId, currentSessionId = msg.Text, upToMessageId: msg.Id);
                    break;
                case "switch_model" when !string.IsNullOrWhiteSpace(msg.Text):
                    agent.LLM.SetModel(msg.Text);
                    break;
                case "enable_approval":
                    agent.Toolbox.UseApproval(async (call, ct) =>
                    {
                        await channel.SendAsync(new("approval_required", Id: call.Id, Name: call.Name, Text: call.Arguments));
                        return true;
                    });
                    break;
                case "disable_approval":
                    agent.Toolbox.RemoveApproval();
                    break;
                case "subagent_query" when !string.IsNullOrWhiteSpace(msg.Text):
                    var sub = agent.With(instructions: [new Text("You are a specialized fast researcher. Answer concisely.")], maxIterations: 5);
                    _ = ExecutePromptAsync(sub, channel, msg.Text, activeCts.Token);
                    break;
                case "cancel":
                    activeCts.Cancel();
                    activeCts = new();
                    await channel.SendAsync(new("cancelled"));
                    break;
                case "prompt" when !string.IsNullOrWhiteSpace(msg.Text):
                    _ = ExecutePromptAsync(agent, channel, msg.Text, activeCts.Token);
                    break;
            }
        }
    }

    private static async Task ExecutePromptAsync(IAgent agent, IIpcChannel channel, string prompt, CancellationToken ct = default)
    {
        try
        {
            await foreach (var evt in agent.InvokeStreamingAsync([new Text(prompt)], ct))
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
        catch (OperationCanceledException) { await channel.SendAsync(new("cancelled")); }
        catch (Exception ex) { await channel.SendAsync(new("error", Text: ex.Message)); }
    }

    private static Config LoadConfig(string root)
    {
        string[] paths = [Path.Combine(root, "config.json"), Path.Combine(root, "AgentCore.Host", "config.json"), Path.Combine(AppContext.BaseDirectory, "config.json")];
        foreach (var p in paths)
            if (File.Exists(p))
                try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(p), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); } catch { }
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
