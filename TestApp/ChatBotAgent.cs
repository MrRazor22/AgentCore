using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Tokens;
using AgentCore.Providers.MEAI;
using AgentCore.Runtime;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Text;

namespace TestApp;

/// <summary>
/// Demo showcasing AgentCore capabilities: streaming, middleware pipeline, tool use, and memory.
/// </summary>
public static class ChatBotAgent
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ─── Setup Agent ───
        var chatStore = new FileMemory(new() { PersistDir = @"D:\AgentCore\chat-history" });
        // Optional: Advanced memory with AMFS (from AgentCore.Memory)
        // var engine = new MemoryEngine(new FileStore(@"D:\AgentCore", "chatbot"), llmProvider);
        
        var agent = LLMAgent.Create("chatbot")
            .WithMemory(chatStore)
            // .WithMemory(engine)   // <- optional: enables semantic memory recall/retain
            .WithInstructions("rules", "You are an AI agent, execute all user requests faithfully.")
            .WithInstructions("persona", "You are helpful, concise and technical.")
            .AddOpenAI("model", "lmstudio", "http://127.0.0.1:1234/v1", new() { ContextLength = 8000, ReasoningEffort = ReasoningEffort.Low })
            
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithTools<SearchTools>()

            .UseToolMiddleware(MonitorToolExecution)
            .UseLLMMiddleware(MonitorLLMStream)
            .WithLoggerFactory(ConfigureLogging())
            .Build();

        var sessionId = "demo-session-001";

        // ─── Load & Display Previous Messages ───
        await LoadPreviousMessages(chatStore, sessionId);

        // ─── Interactive Loop ───
        while (true)
        {
            Console.Write("\n🎯 User: ");
            var goal = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(goal)) continue;

            using var cts = new CancellationTokenSource();
            SetupCancellationHandler(cts);

            try
            {
                await ProcessStream(agent, goal, sessionId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n⚠️  Cancelled");
            }
        }
    }

    // ─── Load Previous Messages ───
    private static async Task LoadPreviousMessages(IChat chatStore, string sessionId)
    {
        var history = await chatStore.RecallAsync(sessionId);
        if (history.Count == 0) return;

        Console.WriteLine("\n" + new string('=', 50));
        Console.WriteLine("📜 Previous conversation loaded:");
        Console.WriteLine(new string('=', 50));

        foreach (var msg in history)
        {
            var text = msg.Contents.OfType<Text>().FirstOrDefault()?.Value ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;

            var roleLabel = msg.Role.ToString() switch
            {
                "User" => "👤 User",
                "Assistant" => "🤖 Assistant",
                "Tool" => "🔧 Tool",
                _ => msg.Role.ToString()
            };

            Console.ForegroundColor = msg.Role == Role.User ? ConsoleColor.Cyan : 
                                       msg.Role == Role.Assistant ? ConsoleColor.Yellow : ConsoleColor.Gray;
            Console.WriteLine($"\n{roleLabel}:");
            Console.ResetColor();
            
            var displayText = text;
            Console.WriteLine(displayText);
        }

        Console.WriteLine("\n" + new string('=', 50));
        Console.WriteLine("Continuing conversation...\n");
    }

    // ─── Stream Processing ───
    private static async Task ProcessStream(LLMAgent agent, string goal, string session, CancellationToken ct)
    {
        var output = new StringBuilder();
        var reasoning = new StringBuilder();
        var isReasoning = false;

        await foreach (var evt in agent.InvokeStreamingAsync((Text)goal, session, ct))
        {
            switch (evt)
            {
                case ReasoningEvent r:
                    reasoning.Append(r.Delta);
                    if (!isReasoning && reasoning.Length > 50)
                    {
                        isReasoning = true;
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write("\n💭 ");
                        Console.ResetColor();
                    }
                    if (isReasoning) Console.Write(r.Delta);
                    break;

                case TextEvent t:
                    if (isReasoning)
                    {
                        Console.WriteLine();
                        isReasoning = false;
                    }
                    Console.Write(t.Delta);
                    output.Append(t.Delta);
                    break;
            }
        }

        if (output.Length == 0)
            Console.WriteLine("⚠️  No response");
    }

    // ─── Middleware: Tool Monitoring ───
    private static async Task<ToolResult> MonitorToolExecution(
        ToolCall call,
        AgentCore.Execution.PipelineHandler<ToolCall, Task<ToolResult>> next,
        CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n🔧 {call.Name}({call.Arguments.ToJsonString()})");
        Console.ResetColor();

        var result = await next(call, ct);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Result: {result?.Result?.ForLlm()}");
        Console.ResetColor();

        return result!;
    }

    // ─── Middleware: Stream Monitoring ───
    private static async IAsyncEnumerable<LLMEvent> MonitorLLMStream(
        LLMCall req,
        AgentCore.Execution.PipelineHandler<LLMCall, IAsyncEnumerable<LLMEvent>> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var events = new List<LLMEvent>();
        await foreach (var evt in next(req, ct).ConfigureAwait(false))
        {
            events.Add(evt);
            yield return evt;
        }

        var tools = events.OfType<ToolCallEvent>().Count();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(tools > 0
            ? $"\n📊 {events.Count} events, {tools} tool calls"
            : $"\n📊 {events.Count} events (complete)");
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
                    Console.WriteLine("\n🛑 Stop requested...");
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