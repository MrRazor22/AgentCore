using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context;

/// <summary>
/// Decorator layer that automatically restores existing context from an <see cref="IContextStore"/>
/// and persists updated messages on modification.
/// </summary>
public sealed class ContextPersistenceLayer(IContextStore store, string sessionId, bool autoRestore = true)
    : ContextLayer, IDisposable
{
    private readonly IContextStore _store = store ?? throw new ArgumentNullException(nameof(store));
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
    /// Explicitly restores context from the store on demand if not already restored.
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
            await Inner.AddAsync(existing, ct).ConfigureAwait(false);
        _restored = true;
    }

    public void Dispose() => _lock.Dispose();
}
