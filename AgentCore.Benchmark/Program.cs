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
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var api = new TornadoApi(new Uri("http://localhost"), "dummy");
        var llm = new TornadoLLMProvider(api, new ChatModel("gpt-4o")); // Using quick setup for benchmark
        var emb = new TornadoEmbeddingProvider(api, new EmbeddingModel("text-embedding-3-small"));
        
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

        var dataPath = "locomo10.json";
        if (!File.Exists(dataPath)) 
        {
            Console.WriteLine("Please download locomo10.json into the working directory.");
            return;
        }

        var bench = new LoCoMoBenchmark(memory, llm, dataPath);
        await bench.RunAsync();
    }
}
