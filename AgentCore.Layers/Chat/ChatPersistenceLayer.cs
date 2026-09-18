using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.Layers.Context.Store;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context;

public sealed class ChatPersistenceLayer(
    IChatStore store,
    IWalStore? walStore = null) : ContextLayer
{
    private readonly IChatStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private bool _restored;

    public override async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        await RestoreAsync(ct).ConfigureAwait(false);
        return await base.ReadAsync(ct).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<IContentEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await RestoreAsync(ct).ConfigureAwait(false);
        int initial = (await Inner.ReadAsync(ct: CancellationToken.None).ConfigureAwait(false)).Count;
        try
        {
            var source = walStore == null ? events : LogAsync();
            async IAsyncEnumerable<IMessageEvent> LogAsync()
            {
                await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
                {
                    await walStore.AppendAsync(evt, ct).ConfigureAwait(false);
                    yield return evt;
                }
            }

            await foreach (var evt in base.WriteAsync(source, ct).WithCancellation(ct).ConfigureAwait(false))
                yield return evt;
        }
        finally
        {
            try
            {
                var history = await Inner.ReadAsync(ct: CancellationToken.None).ConfigureAwait(false);
                var newMessages = history.Skip(initial).Where(m => m.Contents.Count > 0).ToList();
                if (newMessages.Count > 0)
                    await _store.AppendAsync(newMessages, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (walStore != null) await walStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task RestoreAsync(CancellationToken ct)
    {
        if (_restored) return;
        _restored = true;

        if (walStore != null)
        {
            var recovered = await walStore.RecoverAsync(ct).ToMessagesAsync(ct: ct).ConfigureAwait(false);
            if (recovered.Count > 0) await _store.AppendAsync(recovered, ct).ConfigureAwait(false);
            await walStore.ClearAsync(ct).ConfigureAwait(false);
        }

        if (await _store.LoadAsync(ct).ConfigureAwait(false) is { Count: > 0 } history)
        {
            foreach (var m in ExtractWorkingContext(history))
                await Inner.WriteAsync(m, ct).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<Message> ExtractWorkingContext(IReadOnlyList<Message> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
            if (history[i].Get<Summary>() != null)
            {
                var sys = history.FirstOrDefault(m => m.Role == Role.System);
                return sys != null ? [sys, .. history.Skip(i)] : [.. history.Skip(i)];
            }
        return history;
    }
}
