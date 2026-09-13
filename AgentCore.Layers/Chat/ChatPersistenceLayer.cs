using System.Runtime.CompilerServices;
using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

public sealed class ChatPersistenceLayer(IChatStore store, string sessionId, bool autoRestore = true) : ContextLayer
{
    private bool _restored;

    public override async Task<IReadOnlyList<Message>> GetAsync(CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        return await base.GetAsync(ct).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<IMessageEvent> IngestAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        await foreach (var evt in base.IngestAsync(events, ct).ConfigureAwait(false))
        {
            if (evt is Message m)
            {
                await store.AppendAsync(sessionId, [m], ct).ConfigureAwait(false);
            }
            yield return evt;
        }
    }

    private async Task EnsureRestoredAsync(CancellationToken ct)
    {
        if (_restored || !autoRestore) return;
        _restored = true;
        if (await store.LoadAsync(sessionId, ct).ConfigureAwait(false) is { Count: > 0 } history)
        {
            foreach (var msg in ExtractWorkingContext(history))
            {
                await Inner.AppendAsync(msg, ct).ConfigureAwait(false);
            }
        }
    }

    internal static IReadOnlyList<Message> ExtractWorkingContext(IReadOnlyList<Message> history)
    {
        int lastSummary = history.ToList().FindLastIndex(m => m.Get<Summary>() is not null);
        if (lastSummary < 0) return history;
        var system = history.FirstOrDefault(m => m.Role == Role.System);
        return system != null ? [system, .. history.Skip(lastSummary)] : [.. history.Skip(lastSummary)];
    }
}
