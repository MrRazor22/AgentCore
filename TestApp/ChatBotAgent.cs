using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Memory;
using AgentCore.Providers.Tornado;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Text;

namespace TestApp;

/// <summary>
/// Full-featured chatbot showcasing AgentCore capabilities:
/// Streaming, live event monitoring (token/tool status), skills, multi-agent sub-agents,
/// cognitive memory with scoping, and tool approval.
/// </summary>
public static class ChatBotAgent
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var loggerFactory = ConfigureLogging();

        var apiKey = "lmstudio";
        var modelName = "qwen/qwen3.5-9b";
        var baseUrl = new Uri("http://127.0.0.1:1234");

        // ─── Sub-Agent: Research Agent (demonstrates multi-agent via WithAgentTool) ───
        var researchAgent = LLMAgent.Create("researcher")
            .WithProvider(TornadoProvider.CreateLLMProvider(apiKey, modelName, baseUrl), new() { ContextLength = 20000 })
            .WithSystemPrompt("You are a research assistant. When given a topic, provide a thorough, well-structured analysis with key points and conclusions. Be concise but comprehensive.")
            .WithTools<SearchTools>()
            .WithLoggerFactory(loggerFactory)
            .Build();

        // ─── Main Agent ───
        var chatStore = new ChatFileStore(@"D:\AgentCore\chat-history");

        var llmProvider = TornadoProvider.CreateLLMProvider(apiKey, modelName, baseUrl);
        var tokenCounter = new ApproximateTokenCounter();
        
        // Use RollingWindowMemory as default - conversation summarization + rolling window
        var memory = new RollingWindowMemory(
            llmProvider,
            tokenCounter,
            new RollingWindowMemoryOptions { WindowSize = 10, EnableSummarization = true },
            loggerFactory.CreateLogger<RollingWindowMemory>());

        var agent = LLMAgent.Create("chatbot")
            .WithProvider(llmProvider, new() { ContextLength = 50000, ReasoningEffort = ReasoningEffort.Low })
            .WithChatHistory(chatStore)
            .WithMemory(memory)

            // Multi-agent: research agent as a callable tool
            .WithAgentTool(researchAgent, "deep_research",
                "Delegate in-depth research to a specialized research agent. Use when the user asks for thorough analysis, comparisons, or investigation of a topic.")

            // Standard tools
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithTools<SearchTools>()

            .WithLoggerFactory(loggerFactory)
            .Build();

        var sessionId = "demo-session-001";

        // ─── Load & Display Previous Messages ───
        await LoadPreviousMessages(chatStore, sessionId);

        PrintWelcome();

        // ─── Interactive Loop ───
        while (true)
        {
            Console.Write("\n🎯 ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("User: ");
            Console.ResetColor();
            var goal = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(goal)) continue;

            // Commands
            if (goal.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;
            if (goal.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                PrintWelcome();
                continue;
            }
            if (goal.Equals("/session", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("Enter new session ID: ");
                sessionId = Console.ReadLine()?.Trim() ?? sessionId;
                Console.WriteLine($"  Switched to session: {sessionId}");
                continue;
            }

            using var cts = new CancellationTokenSource();
            SetupCancellationHandler(cts);

            try
            {
                await ProcessStream(agent, goal, sessionId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n⚠️  Cancelled");
                Console.ResetColor();
            }
        }
    }

    // ─── Load Previous Messages ───
    private static async Task LoadPreviousMessages(IChatMemory chatStore, string sessionId)
    {
        var history = await chatStore.RecallAsync(sessionId);
        if (history.Count == 0) return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  📜 Loaded {history.Count} messages from session '{sessionId}'");
        Console.ResetColor();

        foreach (var msg in history)
        {
            var text = msg.Contents.OfType<Text>().FirstOrDefault()?.Value ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;

            var (icon, color) = msg.Role switch
            {
                var r when r == Role.User => ("👤", ConsoleColor.Cyan),
                var r when r == Role.Assistant => ("🤖", ConsoleColor.White),
                var r when r == Role.Tool => ("🔧", ConsoleColor.DarkGray),
                _ => ("  ", ConsoleColor.Gray)
            };

            Console.ForegroundColor = color;
            var preview = text.Length > 100 ? text[..100] + "..." : text;
            Console.WriteLine($"  {icon} {preview}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Continuing conversation...\n");
        Console.ResetColor();
    }

    // ─── Stream Processing ───
    private static async Task ProcessStream(LLMAgent agent, string goal, string session, CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  ╭─ Session: {session}");
        Console.ResetColor();

        var output = new StringBuilder();
        var reasoning = new StringBuilder();
        var isReasoning = false;
        var hasOutput = false;

        int inputTokens = 0;
        int outputTokens = 0;

        await foreach (var evt in agent.InvokeStreamingAsync((Text)goal, session, ct))
        {
            switch (evt)
            {
                case ReasoningEvent r:
                    reasoning.Append(r.Delta);
                    if (!isReasoning)
                    {
                        isReasoning = true;
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write("\n  💭 ");
                        Console.ResetColor();
                    }
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    Console.Write(r.Delta);
                    Console.ResetColor();
                    break;

                case TextEvent t:
                    if (isReasoning)
                    {
                        Console.WriteLine();
                        isReasoning = false;
                    }
                    if (!hasOutput)
                    {
                        Console.Write("\n  🤖 ");
                        hasOutput = true;
                    }
                    Console.Write(t.Delta);
                    output.Append(t.Delta);
                    break;

                case ToolCallEvent tc:
                    if (isReasoning)
                    {
                        Console.WriteLine();
                        isReasoning = false;
                    }
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.Write($"\n  │ 🔧 Tool call: {tc.Call.Name}");
                    var args = tc.Call.Arguments?.ToString();
                    if (!string.IsNullOrEmpty(args))
                    {
                        Console.Write($" with args: {(args.Length > 80 ? args[..80] + "..." : args)}");
                    }
                    Console.ResetColor();
                    break;

                case AgentToolResultEvent tr:
                    var isError = tr.Result.Result is ToolExecutionException;
                    Console.ForegroundColor = isError ? ConsoleColor.Red : ConsoleColor.Green;
                    var resultPreview = tr.Result.ForLlm() ?? "";
                    var preview = resultPreview.Length > 60
                        ? resultPreview[..60] + "..."
                        : resultPreview;
                    Console.WriteLine($" {(isError ? "✗" : "✓")} {preview}");
                    Console.ResetColor();
                    break;

                case LLMMetaEvent meta:
                    if (!meta.Usage.IsEmpty)
                    {
                        inputTokens += meta.Usage.InputTokens;
                        outputTokens += meta.Usage.OutputTokens;
                        
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($" [{meta.Usage.InputTokens}↑ {meta.Usage.OutputTokens}↓]");
                        Console.ResetColor();
                    }
                    break;
            }
        }

        if (isReasoning)
        {
            Console.WriteLine();
        }

        if (output.Length > 0) Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  ╰─ 📊 Tokens: {inputTokens + outputTokens} ({inputTokens}↑ {outputTokens}↓)");
        Console.ResetColor();

        if (!hasOutput && output.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  ⚠️  No response text generated.");
            Console.ResetColor();
        }
    }

    // ─── Welcome Screen ───
    private static void PrintWelcome()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("""

          ╔══════════════════════════════════════════════╗
          ║          AgentCore ChatBot v2                ║
          ╠══════════════════════════════════════════════╣
          ║  Features:                                   ║
          ║   • Cognitive Memory (AMFS decay, skills)    ║
          ║   • Live Stream Events (tokens, tool status) ║
          ║   • Multi-Agent (deep_research sub-agent)    ║
          ║   • Authored Skills (code_review)            ║
          ║   • Session Persistence & Recovery           ║
          ╠══════════════════════════════════════════════╣
          ║  Commands: /quit  /clear  /session           ║
          ╚══════════════════════════════════════════════╝
        """);
        Console.ResetColor();
    }

    // ─── Infrastructure ───
    private static void SetupCancellationHandler(CancellationTokenSource cts)
    {
        _ = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    cts.Cancel();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n  🛑 Stop requested...");
                    Console.ResetColor();
                    break;
                }
            }
        });
    }

    private static ILoggerFactory ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(LogEventLevel.Information)
            .WriteTo.File(@"D:\AgentCore\AgentCore.log", LogEventLevel.Verbose, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        return LoggerFactory.Create(b => b.AddSerilog(Log.Logger));
    }
}