using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Context.Primitives;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public interface IContext
{
    Task<IReadOnlyList<Message>> PrepareAsync(IEnumerable<Message>? messages = null, CancellationToken ct = default);
    IAsyncEnumerable<IContentEvent> IngestAsync(IAsyncEnumerable<IMessageEvent> events, CancellationToken ct = default);
}

public class ChatContext(
    int contextWindow = 50000, int? reserveTokens = null, int? maxSingleMessageTokens = null,
    ICompactor? compactor = null, ITokenizer? counter = null, ITruncator? truncator = null,
    IAssembler? assembler = null, ILogger<ChatContext>? logger = null) : IContext
{
    private readonly List<Message> _chat = [];
    private readonly Dictionary<string, IAssembler> _open = new(StringComparer.Ordinal);
    private readonly IAssembler _assembler = assembler ?? new Assembler();
    private readonly ITokenizer _counter = counter ?? new Tokenizer();
    private readonly ITruncator _truncator = truncator ?? new Truncator(counter ?? new Tokenizer());
    private readonly int _limit = Math.Max(1, contextWindow - (reserveTokens ?? Math.Min(4_000, contextWindow / 10)));
    private readonly int _maxTokens = maxSingleMessageTokens ?? Math.Max(125, Math.Min(10_000, contextWindow / 5));
    private readonly object _lock = new();
    private string? _activeId;
    private int _tokens;

    public async IAsyncEnumerable<IContentEvent> IngestAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
            {
                IContent? completedContent;
                lock (_lock) completedContent = AppendLocked(evt);

                if (evt is MessageDelta { Content: { } ce }) yield return ce;
                if (completedContent is not null) yield return completedContent;
            }
        }
        finally
        {
            lock (_lock)
            {
                foreach (var (id, asm) in _open.ToList())
                    if (asm.ToMessage(new MessageEnd(id)) is { Contents.Count: > 0 } msg)
                        Commit(msg);
                _open.Clear();
                _activeId = null;
            }
        }
    }

    private IContent? AppendLocked(IMessageEvent evt)
    {
        if (evt is Message m)
        {
            Commit(m, m.Metadata.Get<TokenUsage>()?.TotalTokens);
            return null;
        }

        var id = evt.Id;

        if (evt is MessageStart ms)
        {
            id ??= Guid.NewGuid().ToString("N");
            if (ms.Role != Role.Tool) _activeId = id;
            _open[id] = _assembler.Create(ms);
            return null;
        }

        id ??= _activeId ??= Guid.NewGuid().ToString("N");
        if (!_open.TryGetValue(id, out var asm))
            asm = _open[id] = _assembler.Create();

        if (evt is MessageEnd me)
        {
            _open.Remove(id);
            if (id == _activeId) _activeId = null;
            var msg = asm.ToMessage(me);
            Commit(msg, msg.Metadata.Get<TokenUsage>()?.TotalTokens);
            return null;
        }

        return evt is MessageDelta md ? asm.Push(md) : null;
    }

    public async Task<IReadOnlyList<Message>> PrepareAsync(IEnumerable<Message>? messages = null, CancellationToken ct = default)
    {
        if (messages is not null)
        {
            lock (_lock)
            {
                foreach (var message in messages)
                    AppendLocked(message);
            }
        }

        List<Message> snapshot;
        lock (_lock) snapshot = [.. _chat];

        if (_tokens > _limit && compactor != null)
        {
            logger?.LogInformation("Context overflow ({Tokens}/{Limit}). Compacting via {Compactor}...", _tokens, _limit, compactor.GetType().Name);
            var compacted = await compactor.CompactAsync(snapshot, _limit, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _chat.Clear();
                _chat.AddRange(compacted);
                _tokens = _chat.Sum(Estimate);
                snapshot = [.. _chat];
            }
            logger?.LogInformation("Compacted: {Count} messages ({Tokens} tokens).", snapshot.Count, _tokens);
        }

        logger?.LogDebug("Context staged: {Count} messages ({Tokens}/{Limit} tokens).", snapshot.Count, _tokens, _limit);
        return snapshot;
    }

    private Message Commit(Message m, int? tokens = null)
    {
        var truncated = m.Role == Role.System ? m : new Message(m.Role, m.Contents.Select(c => _truncator.Truncate(c, _maxTokens)).ToList(), m.Id, m.Metadata);
        _chat.Add(truncated);
        _tokens = tokens ?? (_tokens + Estimate(truncated));
        return truncated;
    }

    private int Estimate(Message m) => (int)((1 + m.Contents.Sum(_counter.Estimate)) * 1.15);
}
