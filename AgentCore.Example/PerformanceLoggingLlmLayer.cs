using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;

namespace AgentCore.Example;

/// <summary>
/// A decorator layer for ILLMService that intercepts the LLM streaming event flow
/// to measure response time and display token usage statistics, including
/// the percentage of the context window currently consumed.
/// </summary>
public class PerformanceLoggingLlmLayer : ILLMService
{
    private readonly ILLMService _inner;
    private readonly ITokenCounter _tokenCounter;
    private readonly int _contextWindow;

    public PerformanceLoggingLlmLayer(ILLMService inner, ITokenCounter tokenCounter, int contextWindow)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _contextWindow = contextWindow;
    }

    public Task<int> GetContextBudgetAsync(IReadOnlyList<Message> prompt, CancellationToken ct = default)
    {
        return _inner.GetContextBudgetAsync(prompt, ct);
    }

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int estimatedInputTokens = await _tokenCounter.EstimateAsync(messages, ct);
        int generatedTokens = 0;

        await foreach (var evt in _inner.StreamAsync(messages, responseSchema, ct))
        {
            if (evt is TokenUsageEvent usage)
            {
                generatedTokens = usage.OutputTokens;
            }

            yield return evt;
        }

        sw.Stop();

        // Calculate and display percentage utilization
        var utilizationPercent = (double)estimatedInputTokens / _contextWindow * 100;

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"\n[Performance Audit] Latency: {sw.ElapsedMilliseconds}ms | Prompt Size: {estimatedInputTokens:#,##0} / {_contextWindow:#,##0} tokens ({utilizationPercent:F2}%) | Generated: {generatedTokens} tokens");
        Console.ResetColor();
    }
}
