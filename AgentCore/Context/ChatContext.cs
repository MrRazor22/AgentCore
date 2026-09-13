using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public interface IContext
{
    Task<IReadOnlyList<Message>> GetAsync(CancellationToken ct = default); 
    Task AppendAsync(IAgentEvent evt, CancellationToken ct = default);
    Task AppendAsync(Message message, CancellationToken ct = default);
}

public class ChatContext(
    int contextWindow = 50000,
    int? reserveTokens = null,
    int? maxSingleMessageTokens = null,
    ICompactor? compactor = null,
    ITokenizer? counter = null,
    ITruncator? truncator = null,
    ILogger<ChatContext>? logger = null) : IContext
{
    private readonly List<Message> _chat = [];
    private readonly Dictionary<string, MessageAssembler> _open = new(StringComparer.Ordinal);
    private readonly ITokenizer _counter = counter ?? new Tokenizer();
    private readonly ITruncator _truncator = truncator ?? new Truncator(counter ?? new Tokenizer());
    private readonly int _limit = Math.Max(1, contextWindow - (reserveTokens ?? Math.Min(4_000, contextWindow / 10)));
    private readonly int _maxTokens = maxSingleMessageTokens ?? Math.Max(125, Math.Min(10_000, contextWindow / 5));
    private readonly object _lock = new();
    private string? _activeId;
    private int _tokens;

    public Task AppendAsync(Message message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_lock)
        {
            if (message.Role == Role.User) StripReasoning();
            Commit(message);
        }
        return Task.CompletedTask;
    }

    public Task AppendAsync(IAgentEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        lock (_lock)
        {
            var id = (evt as IMessageEvent)?.MessageId ?? (evt as IBlockEvent)?.MessageId ?? (_activeId ??= Guid.NewGuid().ToString("N"));

            if (evt is MessageStart ms)
            {
                if (ms.Role == Role.User) StripReasoning();
                _open[id] = new(ms.Role, ms.MessageId, metadata: ms.Metadata);
            }
            else if (_open.TryGetValue(id, out var asm))
            {
                asm.Push(evt);
                if (evt is MessageEnd me)
                {
                    _open.Remove(id);
                    if (id == _activeId) _activeId = null;
                    Commit(asm.ToMessage(), me.Usage?.TotalTokens);
                }
            }
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Message>> GetAsync(CancellationToken ct = default)
    {
        List<Message> snapshot;
        lock (_lock) snapshot = [.. _chat, .. _open.Values.Select(a => a.ToSnapshot())];

        if (_tokens > _limit && compactor != null)
        {
            logger?.LogInformation("Context overflow ({Tokens}/{Limit}). Compacting via {Compactor}...", _tokens, _limit, compactor.GetType().Name);
            var compacted = await compactor.CompactAsync(snapshot, _limit, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _chat.Clear();
                _chat.AddRange(compacted);
                _tokens = _chat.Sum(Estimate);
                snapshot = [.. _chat, .. _open.Values.Select(a => a.ToSnapshot())];
            }
            logger?.LogInformation("Compacted: {Count} messages ({Tokens} tokens).", snapshot.Count, _tokens);
        }

        logger?.LogDebug("Context staged: {Count} messages ({Tokens}/{Limit} tokens).", snapshot.Count, _tokens, _limit);
        return snapshot;
    }

    private void Commit(Message m, int? tokens = null)
    {
        var truncated = m.Role == Role.System ? m : new Message(m.Role, m.Contents.Select(c => _truncator.Truncate(c, _maxTokens)).ToList(), m.Info, m.Metadata);
        _chat.Add(truncated);
        _tokens = tokens ?? (_tokens + Estimate(truncated));
    }

    private void StripReasoning()
    {
        for (int i = 0; i < _chat.Count; i++)
        {
            var kept = _chat[i].Contents.Where(c => c is not Reasoning).ToList();
            if (kept.Count < _chat[i].Contents.Count && kept.Count > 0)
                _chat[i] = new Message(_chat[i].Role, kept, _chat[i].Info, _chat[i].Metadata);
        }
    }

    private int Estimate(Message m) => (int)((1 + m.Contents.Sum(_counter.Estimate)) * 1.15);
}
