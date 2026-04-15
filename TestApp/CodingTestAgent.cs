using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.Runtime;
using AgentCore.CodingAgent;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Tokens;
using AgentCore.Providers.MEAI;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Text;

namespace TestApp;

public static class CodingTestAgent
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var memory = new FileMemory(new() { PersistDir = @"D:\AgentCore\coding-history" });
        // TODO: Wire MemoryEngine: var engine = new MemoryEngine(new FileStore(@"D:\AgentCore", "coding"), llmProvider);

        var agent = CodingAgentBuilder.Create("coding-agent")
            .WithInstructions("You are a helpful coding assistant that solves problems by writing and executing C# code. " +
                "IMPORTANT: Even for simple greetings, you MUST use the Thought/Code format. " +
                "Always respond with a FinalAnswer() call in a ```csharp code block, even for greetings.")
            .WithMemory(memory)
            .WithTokenCounter(new ApproximateTokenCounter())
            .AddOpenAI("model", "lmstudio", "http://127.0.0.1:1234/v1", opts => opts.ContextLength = 8000)
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithTools<SearchTools>()
            .WithLoggerFactory(ConfigureLogging())
            .WithMaxSteps(20)
            .Build();

        var sessionId = "coding-demo-001";

        await LoadPreviousMessages(memory, sessionId);

        while (true)
        {
            Console.Write("\n💻 User: ");
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

    private static async Task LoadPreviousMessages(FileMemory memory, string sessionId)
    {
        var history = await memory.RecallAsync(sessionId);
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

            Console.WriteLine(text);
        }

        Console.WriteLine("\n" + new string('=', 50));
        Console.WriteLine("Continuing conversation...\n");
    }

    private static async Task ProcessStream(CodingAgent agent, string goal, string session, CancellationToken ct)
    {
        var output = new StringBuilder();

        await foreach (var evt in agent.InvokeStreamingAsync(new Text(goal), session, ct))
        {
            switch (evt)
            {
                case AgentCore.CodingAgent.AgentReasoningEvent r:
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write("\n💭 Reasoning: ");
                    Console.ResetColor();
                    Console.WriteLine(r.Reasoning);
                    break;

                case AgentCore.CodingAgent.CodeExecutionEvent c:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\n🔧 Executing code...");
                    Console.ResetColor();
                    if (!string.IsNullOrEmpty(c.Code))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(c.Code);
                        Console.ResetColor();
                    }
                    if (c.Result != null)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("📤 Output: ");
                        Console.ResetColor();
                        Console.WriteLine(c.Result.Output);

                        if (!string.IsNullOrWhiteSpace(c.Result.Logs))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"📋 Logs:\n{c.Result.Logs}");
                            Console.ResetColor();
                        }
                    }
                    break;

                case AgentCore.CodingAgent.CodeErrorEvent err:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n❌ Error: {err.Error}");
                    Console.ResetColor();
                    break;

                case AgentCore.CodingAgent.AgentFinalResultEvent f:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n✅ Final Answer:");
                    Console.ResetColor();
                    Console.WriteLine(f.Result);
                    output.Append(f.Result);
                    break;

                case AgentCore.CodingAgent.AgentMessageEvent m:
                    break;
            }
        }

        if (output.Length == 0)
            Console.WriteLine("⚠️  No response");
    }

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
            .WriteTo.File(@"D:\AgentCore\CodingAgent.log", LogEventLevel.Verbose, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        return LoggerFactory.Create(b => b.AddSerilog(Log.Logger));
    }
}