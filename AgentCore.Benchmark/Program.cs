using AgentCore.Benchmark;
using AgentCore.Memory;
using AgentCore.Providers.Tornado;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Embedding.Models;
using Microsoft.Extensions.Logging;

class Program 
{
    static async Task Main(string[] args)
    {
        // Parse --samples N argument (default: 1)
        int sampleCount = 1;
        var samplesArg = args.FirstOrDefault(a => a.StartsWith("--samples"));
        if (samplesArg != null)
        {
            var parts = samplesArg.Split('=');
            if (parts.Length == 2 && int.TryParse(parts[1], out var n))
            {
                sampleCount = n;
            }
        }

        Console.WriteLine($"=== LoCoMo Benchmark ===");
        Console.WriteLine($"Running {sampleCount} sample(s)");
        if (sampleCount == 1)
        {
            Console.WriteLine("Quick test mode (~19 sessions)");
            Console.WriteLine("Use --samples=N to run more samples (e.g., --samples=10 for full benchmark)");
        }
        else if (sampleCount >= 10)
        {
            Console.WriteLine("Full benchmark mode (288 sessions)");
        }
        Console.WriteLine();

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var apiKey = "lmstudio";
        var modelName = "qwen/qwen3.5-9b";
        var embedModelName = "publisherme/bge/bge-large-en-v1.5-q4_k_m.gguf";
        var baseUrl = new Uri("http://127.0.0.1:1234");
        
        var api = new TornadoApi(baseUrl, apiKey);
        var llm = new TornadoLLMProvider(api, new ChatModel(modelName));
        var emb = new TornadoEmbeddingProvider(api, new EmbeddingModel(embedModelName));
        
        var memDir = Path.Combine(Directory.GetCurrentDirectory(), "benchmark_mem");
        if (Directory.Exists(memDir)) Directory.Delete(memDir, true);
        
        // Use an empty file store for each run
        var store = new FileStore(memDir, "locomo");
        
        var memory = new MemoryEngine(
            store,
            llm,
            emb,
            new MemoryEngineOptions { RecallBudget = 6000 },
            loggerFactory.CreateLogger<MemoryEngine>()
        );

        var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locomo10.json");
        if (!File.Exists(dataPath)) 
        {
            Console.WriteLine($"Could not find dataset at: {dataPath}");
            Console.WriteLine("Please download locomo10.json into the working directory.");
            return;
        }

        var bench = new LoCoMoBenchmark(memory, llm, dataPath, sampleCount);
        await bench.RunAsync();
    }
}
