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

    public override async Task AppendAsync(IMessageEvent evt, CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        await base.AppendAsync(evt, ct).ConfigureAwait(false);
        if (evt is MessageEvent me)
        {
            await store.AppendAsync(sessionId, [me.Message], ct).ConfigureAwait(false);
        }
        else if (evt is MessageEnd)
        {
            var history = await base.GetAsync(ct).ConfigureAwait(false);
            if (history.Count > 0)
            {
                await store.AppendAsync(sessionId, [history[^1]], ct).ConfigureAwait(false);
            }
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
                await Inner.AppendAsync(new MessageEvent(msg), ct).ConfigureAwait(false);
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
