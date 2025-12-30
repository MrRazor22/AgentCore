using AgentCore.BuiltInTools;
using AgentCore.LLM.BuiltInTools;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace TestApp
{
    public static class StructredResponseAgent
    {
        public sealed class AgentAnswer
        {
            public string Answer { get; set; } = "";
            public string? Confidence { get; set; }
        }

        public static async Task RunAsync()
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
                o.MaxRetries = 2;
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

            builder.WithInstructions(
                "You are an AI agent. Always respond using valid JSON matching the response schema."
            );

            builder.WithTools<GeoTools>();
            builder.WithTools<WeatherTool>();
            builder.WithTools<MathTools>();

            var app = builder.Build();

            while (true)
            {
                Console.Write("\nEnter your goal (Ctrl+Q to quit):\n");
                var goal = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(goal))
                    continue;

                using var cts = new CancellationTokenSource();

                Console.WriteLine("\n───────── thinking... ─────────\n");

                try
                {
                    var result = await app.InvokeAsync<AgentAnswer>(
                        goal,
                        cts.Token,
                        chunk =>
                        {
                            if (chunk.Kind == StreamKind.Text)
                                Console.Write(chunk.AsText());
                        }
                    );

                    Console.WriteLine("\n\n───────── FINAL STRUCTURED RESULT ─────────\n");
                    Console.WriteLine(
                        Newtonsoft.Json.JsonConvert.SerializeObject(
                            result,
                            Newtonsoft.Json.Formatting.Indented
                        )
                    );
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("\n[Cancelled]\n");
                }
            }
        }

    }
}