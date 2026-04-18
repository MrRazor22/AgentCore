using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Memory;
using AgentCore.Providers.Tornado;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Text;

namespace TestApp;

/// <summary>
/// Demo showcasing AgentCore capabilities: streaming, tool use, and memory.
/// </summary>
public static class ChatBotAgent
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ─── Setup Agent ───
        // Conversation history (IChat) - stores chat messages per session
        var chatStore = new ChatFileStore(new() { PersistDir = @"D:\AgentCore\chat-history" });
        
        // Semantic memory (IAgentMemory) - AMFS-style memory with confidence decay
        var memoryStore = new FileStore(@"D:\AgentCore\memory", "chatbot");
        var loggerFactory = ConfigureLogging();
        
        // Explicit construction of dependencies
        var tornadoApi = new LlmTornado.TornadoApi("your-api-key"); // Base URL is optional if using default OpenAI
        var modelName = "gpt-4o";
        var embedModel = new LlmTornado.Embedding.Models.EmbeddingModel("text-embedding-3-small");
        var provider = new TornadoLLMProvider(tornadoApi, new LlmTornado.Chat.Models.ChatModel(modelName));
        var embeddings = new TornadoEmbeddingProvider(tornadoApi, embedModel);
        
        var memoryEngine = new MemoryEngine(memoryStore, provider, embeddings, null, null, loggerFactory.CreateLogger<MemoryEngine>());

        // Setup Provider using LLMTornado
        var builder = LLMAgent.Create("chatbot")
            .WithChatHistory(chatStore)
            .WithMemory(memoryEngine)
            .AddTornado(tornadoApi, modelName, embedModel.Name, new() { ContextLength = 8000, ReasoningEffort = ReasoningEffort.Low })
            .WithInstructions("rules", "You are an AI agent, execute all user requests faithfully.")
            .WithInstructions("persona", "You are helpful, concise and technical.")
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithTools<SearchTools>()
            .WithLoggerFactory(loggerFactory);
        
        var agent = builder.Build();

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
    private static async Task LoadPreviousMessages(IChatMemory chatStore, string sessionId)
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