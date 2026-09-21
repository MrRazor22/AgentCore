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
    Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> WriteAsync(IAsyncEnumerable<IMessageEvent> events, CancellationToken ct = default);
}

public sealed class ChatContext(
    int contextWindow = 50000, int? reserveTokens = null, int? maxSingleMessageTokens = null,
    ICompactor? compactor = null, ITokenizer? counter = null, ITruncator? truncator = null,
    IAssembler? assembler = null, INormalizer? normalizer = null, ILogger<ChatContext>? logger = null,
    IEnumerable<Message>? messages = null) : IContext
{
    private Message[] _chat = messages?.ToArray() ?? [];
    private readonly Dictionary<string, IAssembler> _open = new(StringComparer.Ordinal);
    private readonly IAssembler _assembler = assembler ?? new Assembler();
    private readonly ITokenizer _counter = counter ?? new Tokenizer();
    private readonly ITruncator _truncator = truncator ?? new Truncator(counter ?? new Tokenizer());
    private readonly INormalizer _normalizer = normalizer ?? new ChatNormalizer();
    private readonly int _limit = Math.Max(1, contextWindow - (reserveTokens ?? Math.Min(4_000, contextWindow / 10)));
    private readonly int _maxTokens = maxSingleMessageTokens ?? Math.Max(125, Math.Min(10_000, contextWindow / 5));
    private readonly object _lock = new();
    private string? _activeId;
    private int _tokens = messages != null ? messages.Sum(m => (int)((1 + m.Contents.Sum((counter ?? new Tokenizer()).Estimate)) * 1.15)) : 0;

    public async IAsyncEnumerable<IMessageEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
            {
                IMessageEvent? completed;
                lock (_lock) completed = AppendLocked(evt);

                yield return evt;
                if (completed is not null) yield return completed;
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

    private IMessageEvent? AppendLocked(IMessageEvent evt)
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
            return msg;
        }

        return evt is MessageDelta md && asm.Push(md) is { } c ? new MessageDelta(id, Content: c) : null;
    }

    public async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        Message[] snapshot;
        lock (_lock) snapshot = _chat;

        if (_tokens > _limit && compactor != null)
        {
            logger?.LogInformation("Context overflow ({Tokens}/{Limit}). Compacting via {Compactor}...", _tokens, _limit, compactor.GetType().Name);
            var compacted = await compactor.CompactAsync(_normalizer.Normalize(snapshot), _limit, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _chat = [.. compacted, .. _chat.Skip(snapshot.Length)];
                _tokens = _chat.Sum(Estimate);
                snapshot = _chat;
            }
            logger?.LogInformation("Compacted: {Count} messages ({Tokens} tokens).", snapshot.Length, _tokens);
        }

        var normalized = _normalizer.Normalize(snapshot);
        logger?.LogDebug("Context staged: {Count} messages ({Tokens}/{Limit} tokens).", normalized.Count, _tokens, _limit);
        return normalized;
    }

    private Message Commit(Message m, int? tokens = null)
    {
        var truncated = m.Role == Role.System ? m : new Message(m.Role, m.Contents.Select(c => _truncator.Truncate(c, _maxTokens)).ToList(), m.Id, m.Metadata);
        _chat = [.. _chat, truncated];
        _tokens = tokens ?? (_tokens + Estimate(truncated));
        return truncated;
    }

    private int Estimate(Message m) => (int)((1 + m.Contents.Sum(_counter.Estimate)) * 1.15);
}
