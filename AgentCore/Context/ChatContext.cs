using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public class ChatContext : IContext
{
    private readonly List<Message> _chat = new();
    private readonly ICompactor? _compactor;
    private readonly ILogger<ChatContext>? _logger;
    private readonly int _contextWindow, _reserveTokens, _limit, _maxSingleMessageTokens;
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

    public Task AddAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages == null) throw new ArgumentNullException(nameof(messages));
        var compactedMsg = messages.Select(TruncateMessage).ToList();

        lock (_lock)
        {
            foreach (var msg in compactedMsg)
            {
                if (msg.Role == Role.User) StripReasoningFromChat();
                _chat.Add(msg);
            }

            var usage = compactedMsg.FindLast(m => m.Metadata?.Usage != null)?.Metadata?.Usage;
            _committedTokens = usage?.TotalTokens ?? _chat.Sum(Estimate);
        }

        _logger?.LogInformation("Messages added: Added={Added}, Total={Total}, CommittedTokens={Tokens}",
            compactedMsg.Count, _chat.Count, _committedTokens);

        return Task.CompletedTask;
    }

    private void StripReasoningFromChat()
    {
        for (int i = 0; i < _chat.Count; i++)
        {
            if (_chat[i].Contents.Any(c => c is Reasoning))
            {
                var nonReasoning = _chat[i].Contents.Where(c => c is not Reasoning).ToList();
                if (nonReasoning.Count > 0)
                    _chat[i] = new Message(_chat[i].Role, nonReasoning, _chat[i].Metadata);
            }
        }
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(CancellationToken ct = default)
    {
        int estimatedTotal;
        lock (_lock) estimatedTotal = _committedTokens; 

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

        lock (_lock) return _chat.ToList();
    }

    private Message TruncateMessage(Message message)
    {
        if (message.Role is not (Role.Tool or Role.User)) return message;

        return new Message(message.Role, message.Contents.Select(c =>
        {
            int tokens = RequireEstimatable(c);
            if (c is ITruncatable t) return t.Truncate(_maxSingleMessageTokens);
            if (tokens > _maxSingleMessageTokens)
                throw new InvalidOperationException($"Non-truncatable content '{c.GetType().Name}' ({tokens} tokens) exceeds limit of {_maxSingleMessageTokens}.");
            return c;
        }).ToList(), message.Metadata);
    }

    private static int Estimate(Message message) =>
        (int)((1 + message.Contents.Sum(RequireEstimatable)) * SafetyMargin);

    private static int RequireEstimatable(IContent c) =>
        c is IEstimatable e ? e.EstimateTokens() : throw new InvalidOperationException($"Content of type '{c.GetType().Name}' does not implement '{nameof(IEstimatable)}'.");
}

