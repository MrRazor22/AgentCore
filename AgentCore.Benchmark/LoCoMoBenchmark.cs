using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using System.Text;

namespace AgentCore.Benchmark;

public sealed class LoCoMoBenchmark
{
    private readonly MemoryEngine _memory;
    private readonly ILLMProvider _llm;
    private readonly string _datasetPath;

    public LoCoMoBenchmark(MemoryEngine memory, ILLMProvider llm, string datasetPath)
    {
        _memory = memory;
        _llm = llm;
        _datasetPath = datasetPath;
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"Loading dataset from {_datasetPath}...");
        var samples = LoCoMoDataset.Load(_datasetPath); // just testing first 5 or user configured number

        Console.WriteLine($"Loaded {samples.Count} conversations.");

        var scoresByCategory = new Dictionary<int, List<double>>();
        int count = 0;

        foreach (var sample in samples.Take(5))
        {
            Console.WriteLine($"\n--- Processing Conv {sample.SampleId}: {sample.SpeakerA} & {sample.SpeakerB}");

            // 1. Ingestion Phase
            foreach (var session in sample.Sessions)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[Conversation session {session.SessionNum} - {session.Timestamp}]");
                foreach (var turn in session.Turns)
                {
                    sb.AppendLine($"[{session.Timestamp}] {turn.Speaker}: {turn.Text}");
                }
                
                // Directly feed into semantic memory store
                Console.WriteLine($"Ingesting Session {session.SessionNum}...");
                await _memory.RetainAsync([new Message(Role.User, new Text(sb.ToString()))]);
            }

            // 2. QA Phase
            foreach (var qa in sample.QAs)
            {
                // retrieve
                var memories = await _memory.RecallAsync(qa.Question);
                var contextStr = string.Join("\n", memories.Select(m => m.ForLlm()));

                var prompt = $"""
                    You are answering questions about a conversation between two people.
                    Use ONLY the retrieved context below to answer. If the answer is not in the context, output exactly "not mentioned".
                    Keep answers short (1-10 words). Let's go.

                    Context:
                    {contextStr}

                    Question: {qa.Question}
                    Answer:
                    """;

                var answerBuilder = new StringBuilder();
                await foreach (var evt in _llm.StreamAsync([new Message(Role.User, new Text(prompt))], new LLMOptions { MaxOutputTokens = 100 }))
                {
                    if (evt is AgentCore.LLM.TextDelta td)
                    {
                        answerBuilder.Append(td.Value);
                    }
                }
                var answer = answerBuilder.ToString().Trim();
                if (string.IsNullOrEmpty(answer)) answer = "not mentioned";

                var f1 = LoCoMoScoring.Score(answer, qa.GroundTruth, qa.Category);
                
                if (!scoresByCategory.ContainsKey(qa.Category))
                    scoresByCategory[qa.Category] = new List<double>();
                scoresByCategory[qa.Category].Add(f1);

                Console.WriteLine($"[Cat {qa.Category}] Q: {qa.Question}");
                Console.WriteLine($"      A: {answer}");
                Console.WriteLine($"      F1: {f1:F2}");
            }

            count++;
        }

        Console.WriteLine("\n=== Benchmark Results ===");
        double totalSum = 0;
        int totalCount = 0;

        foreach (var kvp in scoresByCategory.OrderBy(k => k.Key))
        {
            var catAvg = kvp.Value.Average();
            Console.WriteLine($"Category {kvp.Key}: {catAvg * 100:F2}% ({kvp.Value.Count} pairs)");
            totalSum += kvp.Value.Sum();
            totalCount += kvp.Value.Count;
        }

        Console.WriteLine($"\nOverall F1: {totalSum / totalCount * 100:F2}%");
    }
}
