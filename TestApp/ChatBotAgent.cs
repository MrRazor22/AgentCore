using AgentCore.BuiltInTools;
using AgentCore.LLM.BuiltInTools;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Providers.OpenAI;
using AgentCore.Runtime;
using AgentCore.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace TestApp
{
    public static class ChatBotAgent
    {
        public static async Task RunAsync()
        {
            var agent = LLMAgent.Create("chatbot")
                .WithInstructions("You are an AI agent, execute all user requests faithfully.")
                .WithModel("model")
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
                .ConfigureServices(services =>
                {
                    services.Configure<ContextBudgetOptions>(o =>
                    {
                        o.MaxContextTokens = 8000;
                        o.Margin = 0.8;
                    });

                    services.Configure<RetryPolicyOptions>(o =>
                    {
                        o.MaxRetries = 2;
                    });

                    services.Configure<AgentMemoryOptions>(o =>
                    {
                        o.PersistDir = @"D:\AgentCore\memory";
                        o.MaxChatHistory = 5;
                    });

                    services.AddLogging(logging =>
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
                    });
                })
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
                    var result = await agent.InvokeAsync(
                        goal,
                        cts.Token,
                        chunk =>
                        {
                            if (chunk.Kind == StreamKind.Text)
                                Console.Write(chunk.AsText());
                        });

                    if (string.IsNullOrWhiteSpace(result.Text))
                    {
                        Console.WriteLine("[no response]");
                        continue;
                    }

                    Console.WriteLine("\n\n\n───────── AGENT RESPONSE ─────────\n");
                    Console.WriteLine(result.Text);
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
