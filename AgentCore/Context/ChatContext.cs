using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public class ChatContext : IContext
{
    private readonly List<Message> _chat = new();
    private readonly ICompactor? _compactor;
    private readonly ILogger<ChatContext>? _logger;

    private readonly int _contextWindow;
    private readonly int _reserveTokens;
    private readonly int _limit;
    private readonly int _maxSingleMessageTokens;
    private int _committedTokens;

    private const double SafetyMargin = 1.15;

    private readonly object _lock = new();

    public ChatContext(
        int contextWindow = 50000, 
        int? reserveTokens = null, 
        int? maxSingleMessageTokens = null,
        ICompactor? compactor = null,
        ILLM? summarizer = null, 
        ILogger<ChatContext>? logger = null)
    {
        _contextWindow = contextWindow;
        _reserveTokens = reserveTokens ?? Math.Min(4_000, contextWindow / 10);
        _limit = Math.Max(1, _contextWindow - _reserveTokens);

        _maxSingleMessageTokens = maxSingleMessageTokens ?? Math.Max(125, Math.Min(10_000, contextWindow / 5));
        _compactor = compactor ?? (summarizer != null ? new Summarizer(summarizer) : null);
        _logger = logger;
    }

    public Task AddAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default)
    {
        if (messages == null) throw new ArgumentNullException(nameof(messages));

        var compactedMsg = messages.Select(TruncateMessage).ToList();

        lock (_lock)
        {
            _chat.AddRange(compactedMsg);

            var lastUsage = compactedMsg.LastOrDefault(m => m.Metadata?.Usage != null)?.Metadata?.Usage;
            _committedTokens = lastUsage != null
                ? (lastUsage.InputTokens + lastUsage.OutputTokens)
                : _committedTokens + compactedMsg.Sum(Estimate);
        }

        _logger?.LogInformation(
            "Messages added to context. AddedCount={AddedCount}, TotalMessages={TotalMessages}, CommittedTokens={CommittedTokens}",
            compactedMsg.Count,
            _chat.Count,
            _committedTokens);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(
        CancellationToken ct = default)
    {
        int estimatedTotal;
        lock (_lock) estimatedTotal = _committedTokens; 

        _logger?.LogInformation(
             "Retrieving context messages. Strategy={Strategy}, TotalMessages={TotalMessages}, EstimatedTokens={EstimatedTokens}, Limit={Limit}",
             _compactor?.GetType().Name ?? "None",
             _chat.Count,
             estimatedTotal,
             _limit);

        if (estimatedTotal > _limit && _compactor != null)
        {
            List<Message> snapshot;
            lock (_lock) snapshot = _chat.ToList();

            var compacted = await _compactor.CompactAsync(snapshot, _limit, ct).ConfigureAwait(false);

            lock (_lock)
            {
                _chat.Clear();
                _chat.AddRange(compacted);
                _committedTokens = _chat.Sum(Estimate);
            }
        }

        lock (_lock) return PruneHistoricalReasoning(_chat); 
    }

    private Message TruncateMessage(Message message)
    {
        if (message.Role is not (Role.Tool or Role.User))
            return message;

        var sanitized = message.Contents.Select(c => c is ITruncatable t ? t.Truncate(_maxSingleMessageTokens) : c).ToList();
        return new Message(message.Role, sanitized, message.Metadata);
    }

    private static IReadOnlyList<Message> PruneHistoricalReasoning(IReadOnlyList<Message> messages)
    {
        int lastUserIndex = -1;
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == Role.User)
            {
                lastUserIndex = i;
                break;
            }
        }

        if (lastUserIndex <= 0)
            return messages;

        var result = new List<Message>(messages.Count);
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i < lastUserIndex && msg.Role == Role.Assistant && msg.Contents.Any(c => c is Reasoning))
            {
                var filtered = msg.Contents.Where(c => c is not Reasoning).ToList();
                if (filtered.Count > 0)
                {
                    result.Add(new Message(msg.Role, filtered, msg.Metadata));
                }
            }
            else
            {
                result.Add(msg);
            }
        }
        return result;
    }

    private static int Estimate(Message message)
    {
        int tokens = 1; // message envelope overhead
        foreach (var content in message.Contents)
        {
            tokens += content is IEstimatable e ? e.EstimateTokens() : 0;
        }
        return (int)(tokens * SafetyMargin);
    }
}
