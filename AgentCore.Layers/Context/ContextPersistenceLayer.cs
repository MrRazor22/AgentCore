using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context;

/// <summary>
/// Decorator layer that automatically restores existing context from an <see cref="IContextStore"/>
/// and persists updated messages on modification.
/// </summary>
public sealed class ContextPersistenceLayer : ContextLayer
{
    private readonly IContextStore _store;
    private readonly string _sessionId;
    private readonly bool _autoRestore;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _restored;

    public ContextPersistenceLayer(IContextStore store, string sessionId, bool autoRestore = true)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId)
            ? sessionId
            : throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));
        _autoRestore = autoRestore;
    }

    public override async Task<IReadOnlyList<Message>> GetMessagesAsync(CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        return await base.GetMessagesAsync(ct).ConfigureAwait(false);
    }

    public override async Task AddAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        await base.AddAsync(messages, ct).ConfigureAwait(false);
        await SaveAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureRestoredAsync(CancellationToken ct)
    {
        if (_restored || !_autoRestore)
            return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_restored)
                return;

            var existing = await _store.LoadAsync(_sessionId, ct).ConfigureAwait(false);
            if (existing is { Count: > 0 })
            {
                await Inner.AddAsync(existing, ct).ConfigureAwait(false);
            }
            _restored = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var messages = await Inner.GetMessagesAsync(ct).ConfigureAwait(false);
            await _store.SaveAsync(_sessionId, messages, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
