using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

/// <summary>
/// Decorator layer that persists conversation history to an <see cref="IChatStore"/>
/// and reconstructs the active working context window on restore by detecting the latest
/// <see cref="CompactedSummary"/> checkpoint to prevent redundant re-summarization.
/// </summary>
public sealed class ChatPersistenceLayer(IChatStore store, string sessionId, bool autoRestore = true)
    : ContextLayer, IDisposable
{
    private readonly IChatStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly string _sessionId = !string.IsNullOrWhiteSpace(sessionId)
        ? sessionId
        : throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _restored;

    public override async Task<IReadOnlyList<Message>> GetMessagesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (autoRestore) await RestoreCoreAsync(ct).ConfigureAwait(false);
            return await base.GetMessagesAsync(ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public override async Task AddAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (autoRestore) await RestoreCoreAsync(ct).ConfigureAwait(false);
            await base.AddAsync(messages, ct).ConfigureAwait(false);
            var snapshot = await Inner.GetMessagesAsync(ct).ConfigureAwait(false);
            await _store.SaveAsync(_sessionId, snapshot, ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Explicitly restores working context from the store on demand if not already restored.
    /// </summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { await RestoreCoreAsync(ct).ConfigureAwait(false); }
        finally { _lock.Release(); }
    }

    private async Task RestoreCoreAsync(CancellationToken ct)
    {
        if (_restored) return;
        var existing = await _store.LoadAsync(_sessionId, ct).ConfigureAwait(false);
        if (existing is { Count: > 0 })
        {
            var workingContext = ExtractWorkingContext(existing);
            await Inner.AddAsync(workingContext, ct).ConfigureAwait(false);
        }
        _restored = true;
    }

    /// <summary>
    /// Reconstructs the working context:
    /// Preserves system message (if any), finds the latest CompactedSummary checkpoint,
    /// and takes that summary plus all messages that followed it.
    /// </summary>
    internal static IReadOnlyList<Message> ExtractWorkingContext(IReadOnlyList<Message> history)
    {
        if (history.Count == 0) return history;

        int latestSummaryIndex = -1;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Contents.Any(c => c is CompactedSummary))
            {
                latestSummaryIndex = i;
                break;
            }
        }

        if (latestSummaryIndex == -1)
            return history;

        var context = new List<Message>();
        var systemIndex = -1;
        for (int i = 0; i < latestSummaryIndex; i++)
        {
            if (history[i].Role == Role.System)
            {
                systemIndex = i;
                break;
            }
        }
        if (systemIndex >= 0)
        {
            context.Add(history[systemIndex]);
        }

        for (int i = latestSummaryIndex; i < history.Count; i++)
        {
            context.Add(history[i]);
        }

        return context;
    }

    public void Dispose() => _lock.Dispose();
}
