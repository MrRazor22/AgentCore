using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Providers.MEAI;
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
            async IAsyncEnumerable<LLMEvent> StreamAndLogEvents(LLMCall req, AgentCore.Execution.PipelineHandler<LLMCall, IAsyncEnumerable<LLMEvent>> next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                var events = new List<LLMEvent>();
                await foreach (var evt in next(req, ct).ConfigureAwait(false))
                {
                    events.Add(evt);
                    yield return evt;
                }
                var toolCalls = events.OfType<ToolCallEvent>().Count();
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(toolCalls > 0
                    ? $"\n  [Model] {events.Count} events, {toolCalls} tool call(s) requested"
                    : $"\n  [Model] {events.Count} events (final response)");
                Console.ForegroundColor = originalColor;
            }

            var agent = LLMAgent.Create("chatbot")
                .WithInstructions("You are an AI agent, execute all user requests faithfully.")
                .WithProvider(MEAILLMClient.Create("http://127.0.0.1:1234/v1", "model", "lmstudio"), new LLMOptions
                {
                    ContextLength = 8000,
                    ReasoningEffort = AgentCore.LLM.ReasoningEffort.High
                })
                .WithTools<GeoTools>()
                .WithTools<WeatherTool>()
                .WithTools<ConversionTools>()
                .WithTools<MathTools>()
                .WithTools<SearchTools>()
                .UseToolMiddleware(async (call, next, ct) =>
                {
                    var originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n  [Tool Executing] -> {call.Name}({call.Arguments.ToJsonString()})");
                    Console.ForegroundColor = originalColor;
                    
                    var result = await next(call, ct);
                    
                    originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  [Tool Result] <- {result?.Result?.ForLlm()}");
                    Console.ForegroundColor = originalColor;
                    
                    return result!;
                })
                .UseLLMMiddleware((req, next, ct) => StreamAndLogEvents(req, next, ct))
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

            var sessionId = Guid.NewGuid().ToString("N");

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
                    var reasoningSb = new StringBuilder();
                    var reasoningShown = false;

                    await foreach (var evt in agent.InvokeStreamingAsync((AgentCore.Conversation.Text)goal, sessionId, cts.Token))
                    {
                        if (evt is ReasoningEvent reasoningEvt)
                        {
                            reasoningSb.Append(reasoningEvt.Delta);
                            
                            if (!reasoningShown && reasoningSb.Length > 50)
                            {
                                var originalColor = Console.ForegroundColor;
                                Console.ForegroundColor = ConsoleColor.Magenta;
                                Console.Write($"\n[Reasoning] ");
                                Console.ForegroundColor = originalColor;
                                reasoningShown = true;
                            }
                            
                            if (reasoningShown)
                            {
                                Console.Write(reasoningEvt.Delta);
                            }
                        }
                        else if (evt is TextEvent textEvt)
                        {
                            if (reasoningShown && reasoningSb.Length > 0)
                            {
                                Console.WriteLine();
                                reasoningSb.Clear();
                            }
                            reasoningShown = false;
                            Console.Write(textEvt.Delta);
                            sb.Append(textEvt.Delta);
                        }
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
