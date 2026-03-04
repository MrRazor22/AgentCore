using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Providers.OpenAI;
using AgentCore.Runtime;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;

namespace TestApp
{
    public static class ChatBotAgent
    {
        public static async Task RunAsync()
        {
            var agent = LLMAgent.Create("chatbot")
                .WithInstructions("You are an AI agent, execute all user requests faithfully.")
                .AddOpenAI(o =>
                {
                    o.BaseUrl = "http://127.0.0.1:1234/v1";
                    o.ApiKey = "lmstudio";
                    o.Model = "model";
                })
                .WithTools<GeoTools>()
                .WithTools<WeatherTool>()
                .WithTools<ConversionTools>()
                .WithTools<MathTools>()
                .WithTools<SearchTools>()
                .BeforeToolCall(async (call, ct) =>
                {
                    var originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n  [Tool Executing] -> {call.Name}({call.Arguments.ToJsonString()})");
                    Console.ForegroundColor = originalColor;
                    return null;
                })
                .AfterToolCall(async (call, result, ct) =>
                {
                    var originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  [Tool Result] <- {result?.AsJsonString()}");
                    Console.ForegroundColor = originalColor;
                    return null;
                })
                .AfterModelCall(async (events, ct) =>
                {
                    var toolCalls = events.OfType<ToolCallEvent>().Count();
                    var originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(toolCalls > 0
                        ? $"\n  [Model] {events.Count} events, {toolCalls} tool call(s) requested"
                        : $"\n  [Model] {events.Count} events (final response)");
                    Console.ForegroundColor = originalColor;
                })
                .WithLoggerFactory(Microsoft.Extensions.Logging.LoggerFactory.Create(logging =>
                {
                    logging.ClearProviders();

                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Verbose()
                        .WriteTo.Console(Serilog.Events.LogEventLevel.Information)
                        .WriteTo.File(
                            @"D:\AgentCore\AgentCore.log",
                            Serilog.Events.LogEventLevel.Verbose,
                            rollingInterval: RollingInterval.Day)
                        .CreateLogger();
                    Log.Debug("Logger initialized");
                    logging.AddSerilog(dispose: true);
                }))
                .Build();

            while (true)
            {
                Console.Write("Enter your goal (Ctrl+Q to quit):\n");

                var goal = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(goal))
                    continue;

                using var cts = new CancellationTokenSource();

                _ = Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Q &&
                            key.Modifiers.HasFlag(ConsoleModifiers.Control))
                        {
                            cts.Cancel();
                            Console.WriteLine("\n-> Cancel requested.");
                            break;
                        }
                    }
                });

                Console.WriteLine("\n\n───────── thinking... ─────────\n");

                try
                {
                    var sb = new StringBuilder();

                    await foreach (var chunk in agent.InvokeStreamingAsync(goal, ct: cts.Token))
                    {
                        Console.Write(chunk);
                        sb.Append(chunk);
                    }

                    var result = sb.ToString();

                    if (string.IsNullOrWhiteSpace(result))
                    {
                        Console.WriteLine("[no response]");
                        continue;
                    }

                    Console.WriteLine("\n\n\n───────── AGENT RESPONSE ─────────\n");
                    Console.WriteLine(result);
                    Console.WriteLine("\n──────────────────────────\n");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("\n[Cancelled]\n");
                }
            }
        }
    }
}
