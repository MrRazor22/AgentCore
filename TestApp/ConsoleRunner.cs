using AgentCore.BuiltInTools;
using AgentCore.LLM.BuiltInTools;
using AgentCore.LLM.Client;
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
                var builder = new AgentBuilder();

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

                builder.Services.Configure<RetryPolicyOptions>(o =>
                {
                    o.MaxRetries = 5;
                });

                builder.AddFileMemory(o =>
                {
                    o.PersistDir = "D:\\AgentCore\\memory";
                });

                builder.Services.AddLogging(logging =>
                {
                    logging.ClearProviders();

                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Verbose()
                        .WriteTo.Console(Serilog.Events.LogEventLevel.Information) // console
                        .WriteTo.File(
                            "D:\\AgentCore\\AgentCore.log",
                            Serilog.Events.LogEventLevel.Verbose,
                            rollingInterval: RollingInterval.Day) // file
                        .CreateLogger();
                    Log.Debug("Logger initialized");

                    logging.AddSerilog(dispose: true);
                });

                builder.Services.Configure<LoggerFilterOptions>(o =>
                {
                    o.MinLevel = LogLevel.Debug;
                });

                builder.Services.Configure<LoggerFilterOptions>(o =>
                {
                    o.MinLevel = LogLevel.Debug;
                });

                builder.WithInstructions(
                    "You are an AI agent, execute all user requests faithfully."
                );

                builder.WithTools<GeoTools>();
                builder.WithTools<WeatherTool>();
                builder.WithTools<ConversionTools>();
                builder.WithTools<MathTools>();
                builder.WithTools<SearchTools>();

                app = builder.Build();

                while (true)
                {
                    Console.Write("Enter your goal (Ctrl+Q to quit):\n");

                    string goal = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(goal))
                        continue;

                    using var cts = new CancellationTokenSource();

                    // cancel watcher
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
                        var result = await app.InvokeAsync(
                            goal,
                            cts.Token,
                            s => Console.Write(s)
                        );

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
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}