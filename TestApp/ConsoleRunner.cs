using AgentCore.BuiltInTools;
using AgentCore.LLM.BuiltInTools;
using AgentCore.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace TestApp
{
    public static class ConsoleRunner
    {
        public static async Task RunAsync()
        {
            Agent? app = null;
            try
            {
                var builder = Agent.CreateBuilder();

                builder.AddContextTrimming(o =>
                {
                    o.MaxContextTokens = 8000;
                    o.Margin = 0.8;
                });

                builder.AddOpenAI(opts =>
                {
                    opts.BaseUrl = "http://127.0.0.1:1234/v1";
                    opts.ApiKey = "lmstudio";
                    opts.Model = "model";
                });
                builder.AddRetryPolicy(o =>
                {
                    o.MaxRetries = 3;
                    o.Timeout = TimeSpan.FromMinutes(5);
                });
                builder.AddFileMemory(o =>
                {
                    o.PersistDir = "D:\\agenty\\memory";
                });

                builder.Services.AddLogging(logging =>
                {
                    logging.ClearProviders();

                    Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.File("D:\\agenty\\agent.log", rollingInterval: RollingInterval.Day)
                    .CreateLogger();
                    Log.Information("Logger initialized");

                    logging.AddSerilog();
                });

                app = builder.Build();

                app.WithInstructions(
                     "You are an AI agent, execute all user requests faithfully."
                 )
                .WithTools<GeoTools>()
                .WithTools<WeatherTool>()
                .WithTools<ConversionTools>()
                .WithTools<MathTools>()
                .WithTools<SearchTools>()

                .UseExecutor(() => new ToolCallingLoop());

                while (true)
                {
                    Console.Write("Enter your goal (Ctrl+Q to quit):\n");

                    // normal input -> backspace works
                    string goal = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(goal))
                        continue;

                    using var cts = new CancellationTokenSource();

                    // cancel watcher (does NOT touch input editing)
                    _ = Task.Run(() =>
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            var key = Console.ReadKey(intercept: true);
                            if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
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
                        var result = await app.InvokeAsync(goal, cts.Token, s => Console.Write(s));

                        var msg = result.Message?.Trim();
                        if (string.IsNullOrWhiteSpace(msg))
                        {
                            Console.WriteLine("[no response]");
                            continue;
                        }

                        Console.WriteLine("\n\n\n───────── AGENT RESPONSE ─────────\n");
                        Console.WriteLine(msg);
                        Console.WriteLine("\n──────────────────────────\n");
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("\n[Cancelled]\n");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
