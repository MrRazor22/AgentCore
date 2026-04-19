using AgentCore.Conversation;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;

namespace AgentCore.Tokens;

public interface IContextCompactor
{
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

        _logger.LogInformation("Context compaction triggered: {Used}/{Limit} ({Pct:F1}%)", totalTokens, ctxLen, usage * 100);

        // Find the last summary message to preserve recent context
        int startIndex = 0;
        for (int i = chat.Count - 1; i >= 0; i--)
        {
            if ((chat[i].Kind & MessageKind.Summary) != 0)
            {
                startIndex = i;
                break;
            }
        }

        // Keep the most recent messages (at least 4)
        int keepCount = Math.Max(4, chat.Count - startIndex);
        int removeCount = chat.Count - keepCount;

        if (removeCount <= 0) return chat;

        chat.RemoveRange(0, removeCount);

        int after = await _counter.CountAsync(chat.GetActiveWindow(), ct).ConfigureAwait(false);
        _logger.LogDebug("Compacted [truncate]: {Before}→{After} ({Saved:P0})",
            totalTokens, after, 1.0 - (double)after / totalTokens);

        return chat;
    }
}
