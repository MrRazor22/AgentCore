using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Providers.Tornado;
using AgentCore.Tooling;
using System.Text;

namespace TestApp;

public static class ChatBotAgent
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var apiKey = "lmstudio";
        var model = "qwen/qwen3.5-9b";
        var baseUrl = new Uri("http://127.0.0.1:1234");

        var agent = LLMAgent.Create("chat")
            .WithProvider(
                TornadoProvider.CreateLLMProvider(apiKey, model, baseUrl),
                new()
                {
                    ContextWindow = 50000
                })
            //.WithTools<MathTools>()
            //.WithTools<SearchTools>()
            .WithTools<WeatherTool>()
            //.WithTools<GeoTools>()
            //.WithTools<ConversionTools>()
            .Build();

        var sessionId = Guid.NewGuid().ToString("N");

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("\n👤 You: ");
            Console.ResetColor();

            var prompt = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(prompt))
                continue;

            if (prompt.Equals("/quit", StringComparison.OrdinalIgnoreCase))
                break;

            if (prompt.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                sessionId = Guid.NewGuid().ToString("N");
                continue;
            }


            _assistantStarted = false;
            _thinkingStarted = false;

            await foreach (var evt in agent.InvokeStreamingAsync((Text)prompt, sessionId))
            {
                Render(evt);
            }

            Console.WriteLine();
        }
    }
    private static bool _assistantStarted;
    private static bool _thinkingStarted;

    private static void Render(AgentEvent evt)
    {
        switch (evt)
        {
            case ReasoningEvent r:
                if (!_thinkingStarted)
                {
                    _thinkingStarted = true;
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    Console.Write("💭 Thinking: ");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.Write(r.Delta);
                Console.ResetColor();
                break;

            case ToolCallEvent t:
                if (_thinkingStarted)
                {
                    Console.WriteLine();
                    _thinkingStarted = false;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n🔧 Tool: {t.Call.Name}");
                Console.ResetColor();
                break;

            case AgentToolResultEvent t:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"📦 Result: {t.Result}");
                Console.ResetColor();
                break;

            case TextEvent t:
                if (_thinkingStarted)
                {
                    Console.WriteLine();
                    _thinkingStarted = false;
                }

                if (!_assistantStarted)
                {
                    _assistantStarted = true;
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write("🤖 Assistant: ");
                    Console.ResetColor();
                }

                Console.Write(t.Delta);
                break;
        }
    }
}