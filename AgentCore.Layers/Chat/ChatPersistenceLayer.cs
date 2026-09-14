using System.Runtime.CompilerServices;
using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

public sealed class ChatPersistenceLayer(IChatStore store, string sessionId, bool autoRestore = true) : ContextLayer
{
    private bool _restored;

    public override async Task<IReadOnlyList<Message>> PrepareAsync(
        IEnumerable<Message>? messages = null,
        CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        if (messages is not null)
        {
            var list = messages as IReadOnlyList<Message> ?? messages.ToList();
            if (list.Count > 0)
            {
                await store.AppendAsync(sessionId, list, ct).ConfigureAwait(false);
            }
        }
        return await base.PrepareAsync(messages, ct).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<IContentEvent> IngestAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        await foreach (var evt in base.IngestAsync(events, ct).ConfigureAwait(false))
        {
            yield return evt;

            if (evt is IContent)
            {
                var history = await base.PrepareAsync(ct: ct).ConfigureAwait(false);
                if (history.Count > 0)
                {
                    await store.AppendAsync(sessionId, [history[^1]], ct).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task EnsureRestoredAsync(CancellationToken ct)
    {
        if (_restored || !autoRestore) return;
        _restored = true;
        if (await store.LoadAsync(sessionId, ct).ConfigureAwait(false) is { Count: > 0 } history)
        {
            await Inner.PrepareAsync(ExtractWorkingContext(history), ct).ConfigureAwait(false);
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
