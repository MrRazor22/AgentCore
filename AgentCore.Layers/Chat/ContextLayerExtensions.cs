using AgentCore;
using AgentCore.Context;
using AgentCore.Layers.Context.Store;

namespace AgentCore.Layers.Context;

public static class ContextLayerExtensions
{
    public static IContext UseSession(
        this IContext context,
        string storageDirectory,
        string sessionId,
        bool enableWal = true)
        => context.UseSession(
            new FileChatStore(storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory)), sessionId ?? throw new ArgumentNullException(nameof(sessionId))),
            enableWal ? new FileWalStore(storageDirectory, sessionId) : null);

    public static IContext UseSession(
        this IContext context,
        IChatStore store,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        return context.AddLayer(new ChatPersistenceLayer(store, walStore, context));
    }

    public static async Task<IContext> ForkSessionAsync(
        this IContext context,
        string storageDirectory,
        string sourceSessionId,
        string newSessionId,
        string? upToMessageId = null,
        bool enableWal = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sourceStore = new FileChatStore(storageDirectory, sourceSessionId);
        var history = await sourceStore.LoadAsync(ct).ConfigureAwait(false);
        var snapshot = history?.Snapshot(upToMessageId);
        var targetStore = new FileChatStore(storageDirectory, newSessionId);
        if (snapshot is { Count: > 0 })
            await targetStore.AppendAsync(snapshot, ct).ConfigureAwait(false);
        return context.UseSession(targetStore, enableWal ? new FileWalStore(storageDirectory, newSessionId) : null);
    }

    public static IContext RemoveSession(this IContext context)
        => context.RemoveLayer<ChatPersistenceLayer>();
}
