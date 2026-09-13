using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public interface IContext
{
    Task<IReadOnlyList<Message>> GetAsync(CancellationToken ct = default); 
    IAsyncEnumerable<IMessageEvent> IngestAsync(IAsyncEnumerable<IMessageEvent> events, CancellationToken ct = default);
}

public class ChatContext(
    int contextWindow = 50000,
    int? reserveTokens = null,
    int? maxSingleMessageTokens = null,
    ICompactor? compactor = null,
    ITokenizer? counter = null,
    ITruncator? truncator = null,
    Func<Role, string?, IMessageAssembler>? assemblerFactory = null,
    ILogger<ChatContext>? logger = null) : IContext
{
    private readonly List<Message> _chat = [];
    private readonly Dictionary<string, IMessageAssembler> _open = new(StringComparer.Ordinal);
    private readonly Func<Role, string?, IMessageAssembler> _assemblerFactory = assemblerFactory ?? ((r, id) => new MessageAssembler(r, id));
    private readonly ITokenizer _counter = counter ?? new Tokenizer();
    private readonly ITruncator _truncator = truncator ?? new Truncator(counter ?? new Tokenizer());
    private readonly int _limit = Math.Max(1, contextWindow - (reserveTokens ?? Math.Min(4_000, contextWindow / 10)));
    private readonly int _maxTokens = maxSingleMessageTokens ?? Math.Max(125, Math.Min(10_000, contextWindow / 5));
    private readonly object _lock = new();
    private string? _activeId;
    private int _tokens;

    public async IAsyncEnumerable<IMessageEvent> IngestAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
        {
            IContent? completedContent = null;
            lock (_lock)
            {
                completedContent = AppendLocked(evt);
            }

            yield return evt;

            if (completedContent is ToolCall tc)
            {
                yield return tc;
            }

            if (evt is MessageEnd)
            {
                Message? completedMsg = null;
                lock (_lock)
                {
                    if (_chat.Count > 0) completedMsg = _chat[^1];
                }
                if (completedMsg is not null)
                {
                    yield return completedMsg;
                }
            }
        }
    }

    private IContent? AppendLocked(IMessageEvent evt)
    {
        if (evt is Message m)
        {
            if (m.Role == Role.User) StripReasoning();
            Commit(m, m.Metadata.Get<TokenUsage>()?.TotalTokens);
            return null;
        }

        var id = evt.Id;

        if (evt is MessageStart ms)
        {
            id ??= Guid.NewGuid().ToString("N");
            if (ms.Role != Role.Tool) _activeId = id;
            if (ms.Role == Role.User) StripReasoning();
            _open[id] = _assemblerFactory(ms.Role, ms.Id);
            return null;
        }

        id ??= _activeId ??= Guid.NewGuid().ToString("N");
        if (!_open.TryGetValue(id, out var asm))
        {
            asm = _open[id] = _assemblerFactory(Role.Assistant, id);
        }

        var content = asm.Push(evt);
        if (evt is MessageEnd)
        {
            _open.Remove(id);
            if (id == _activeId) _activeId = null;
            var msg = asm.ToMessage();
            Commit(msg, msg.Metadata.Get<TokenUsage>()?.TotalTokens);
        }
        return content;
    }

    public async Task<IReadOnlyList<Message>> GetAsync(CancellationToken ct = default)
    {
        List<Message> snapshot;
        lock (_lock) snapshot = [.. _chat, .. _open.Values.Select(a => a.ToMessage())];

        if (_tokens > _limit && compactor != null)
        {
            logger?.LogInformation("Context overflow ({Tokens}/{Limit}). Compacting via {Compactor}...", _tokens, _limit, compactor.GetType().Name);
            var compacted = await compactor.CompactAsync(snapshot, _limit, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _chat.Clear();
                _chat.AddRange(compacted);
                _tokens = _chat.Sum(Estimate);
                snapshot = [.. _chat, .. _open.Values.Select(a => a.ToMessage())];
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

    private void StripReasoning()
    {
        for (int i = 0; i < _chat.Count; i++)
        {
            var kept = _chat[i].Contents.Where(c => c is not Reasoning).ToList();
            if (kept.Count < _chat[i].Contents.Count && kept.Count > 0)
                _chat[i] = new Message(_chat[i].Role, kept, _chat[i].Id, _chat[i].Metadata);
        }
    }

    private int Estimate(Message m) => (int)((1 + m.Contents.Sum(_counter.Estimate)) * 1.15);
}

public static class ContextExtensions
{
    public static async Task AppendAsync(this IContext context, IMessageEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evt);
        await foreach (var _ in context.IngestAsync(ToAsync(evt), ct).ConfigureAwait(false)) { }
    }

    public static async Task AppendAsync(this IContext context, IEnumerable<IMessageEvent> events, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(events);
        await foreach (var _ in context.IngestAsync(ToAsync(events), ct).ConfigureAwait(false)) { }
    }

    public static IAsyncEnumerable<IMessageEvent> IngestAsync(this IContext context, IMessageEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evt);
        return context.IngestAsync(ToAsync(evt), ct);
    }

    private static async IAsyncEnumerable<IMessageEvent> ToAsync(IMessageEvent evt)
    {
        yield return evt;
    }

    private static async IAsyncEnumerable<IMessageEvent> ToAsync(IEnumerable<IMessageEvent> events)
    {
        foreach (var evt in events) yield return evt;
    }
}
