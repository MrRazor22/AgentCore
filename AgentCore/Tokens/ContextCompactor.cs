using AgentCore.Conversation;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;

namespace AgentCore.Tokens;

public interface IContextCompactor
{
    /// <summary>
    /// Reduces the chat history to fit within the specified token budget.
    /// Potentially mutates the provided list or returns a new one.
    /// </summary>
    Task<List<Message>> ReduceAsync(List<Message> chat, int totalTokens, LLMOptions options, CancellationToken ct = default);
}
public sealed class TruncatingContextCompactor : IContextCompactor
{
    private readonly ITokenCounter _counter;
    private readonly ILogger<TruncatingContextCompactor> _logger;
    private readonly double _threshold;

    public TruncatingContextCompactor(
        ITokenCounter counter,
        ILogger<TruncatingContextCompactor> logger,
        double threshold = 0.75)
    {
        _counter = counter;
        _logger = logger;
        _threshold = threshold;
    }

    public async Task<List<Message>> ReduceAsync(List<Message> chat, int totalTokens, LLMOptions options, CancellationToken ct = default)
    {
        int ctxLen = options.ContextLength ?? throw new InvalidOperationException("ContextLength is required.");
        double usage = ctxLen > 0 && totalTokens > 0 ? (double)totalTokens / ctxLen : 0;
        _logger.LogDebug("Context check: {Used}/{Ctx} ({Usage:P0})", totalTokens, ctxLen, usage);

        if (usage < _threshold) return chat;

        _logger.LogInformation("Context limit reached: {Used}/{Limit} ({Pct:F1}%). Truncating oldest messages.", totalTokens, ctxLen, usage * 100);

        // Simple FIFO truncation: drop oldest 25% of messages
        int dropCount = Math.Max(1, chat.Count / 4);
        if (chat.Count > dropCount)
        {
            chat.RemoveRange(0, dropCount);
        }

        int after = await _counter.CountAsync(chat, ct).ConfigureAwait(false);
        _logger.LogDebug("Compacted [truncate]: {Before}→{After} ({Saved:P0})",
            totalTokens, after, 1.0 - (double)after / totalTokens);

        return chat;
    }
}
