using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

public sealed class ChatPersistenceLayer(IChatStore store, string sessionId, bool autoRestore = true) : ContextLayer
{
    private bool _restored;

    public override async Task<IReadOnlyList<Message>> GetMessagesAsync(CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        return await base.GetMessagesAsync(ct).ConfigureAwait(false);
    }

    public override async Task AddAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        await base.AddAsync(messages, ct).ConfigureAwait(false);
        await store.AppendAsync(sessionId, messages, ct).ConfigureAwait(false);
    }

    private async Task EnsureRestoredAsync(CancellationToken ct)
    {
        if (_restored || !autoRestore) return;
        _restored = true;
        if (await store.LoadAsync(sessionId, ct).ConfigureAwait(false) is { Count: > 0 } history)
            await Inner.AddAsync(ExtractWorkingContext(history), ct).ConfigureAwait(false);
    }

    internal static IReadOnlyList<Message> ExtractWorkingContext(IReadOnlyList<Message> history)
    {
        int lastSummary = history.ToList().FindLastIndex(m => m.Get<SummaryMetadata>() is not null);
        if (lastSummary < 0) return history;
        var system = history.FirstOrDefault(m => m.Role == Role.System);
        return system != null ? [system, .. history.Skip(lastSummary)] : [.. history.Skip(lastSummary)];
    }
}
