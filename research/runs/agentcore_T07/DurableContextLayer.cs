using System.Runtime.CompilerServices;
using AgentCore;
using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.LLM.Chat;

namespace AgentCoreT07;

public interface IEventStore
{
    Task AppendWalAsync(IMessageEvent evt, CancellationToken ct = default);
    Task CommitCheckpointAsync(IReadOnlyList<Message> snapshot, CancellationToken ct = default);
    Task ClearWalAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IMessageEvent>> ReadWalAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Message>> LoadCheckpointAsync(CancellationToken ct = default);
}

public sealed class InMemoryEventStore : IEventStore
{
    private readonly List<IMessageEvent> _wal = new();
    private List<Message> _checkpoint = new();
    private readonly object _lock = new();

    public Task AppendWalAsync(IMessageEvent evt, CancellationToken ct = default)
    {
        lock (_lock) _wal.Add(evt);
        return Task.CompletedTask;
    }

    public Task CommitCheckpointAsync(IReadOnlyList<Message> snapshot, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _checkpoint = new List<Message>(snapshot);
            _wal.Clear();
        }
        return Task.CompletedTask;
    }

    public Task ClearWalAsync(CancellationToken ct = default)
    {
        lock (_lock) _wal.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IMessageEvent>> ReadWalAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<IMessageEvent>>([.. _wal]);
    }

    public Task<IReadOnlyList<Message>> LoadCheckpointAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<Message>>([.. _checkpoint]);
    }

    public int WalCount { get { lock (_lock) return _wal.Count; } }
}

public sealed class DurableContextLayer(IEventStore store, IContext? inner = null) : ContextLayer(inner)
{
    private readonly IEventStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private bool _recovered;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public override async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        await EnsureRecoveredAsync(ct).ConfigureAwait(false);
        return await base.ReadAsync(ct).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<IContentEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureRecoveredAsync(ct).ConfigureAwait(false);

        async IAsyncEnumerable<IMessageEvent> LoggingStream()
        {
            await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
            {
                await _store.AppendWalAsync(evt, ct).ConfigureAwait(false);
                yield return evt;
            }
        }

        await foreach (var evt in base.WriteAsync(LoggingStream(), ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        var snapshot = await Inner.ReadAsync(ct).ConfigureAwait(false);
        await _store.CommitCheckpointAsync(snapshot, ct).ConfigureAwait(false);
    }

    public async Task EnsureRecoveredAsync(CancellationToken ct = default)
    {
        if (_recovered) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_recovered) return;

            var checkpoint = await _store.LoadCheckpointAsync(ct).ConfigureAwait(false);
            if (checkpoint.Count > 0)
            {
                foreach (var msg in checkpoint)
                {
                    await Inner.WriteAsync(msg, ct).ConfigureAwait(false);
                }
            }

            var walEntries = await _store.ReadWalAsync(ct).ConfigureAwait(false);
            if (walEntries.Count > 0)
            {
                await foreach (var _ in Inner.WriteAsync(ToAsyncEnumerable(walEntries), ct).ConfigureAwait(false)) { }
                var recoveredSnapshot = await Inner.ReadAsync(ct).ConfigureAwait(false);
                await _store.CommitCheckpointAsync(recoveredSnapshot, ct).ConfigureAwait(false);
            }

            _recovered = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async IAsyncEnumerable<IMessageEvent> ToAsyncEnumerable(IEnumerable<IMessageEvent> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
