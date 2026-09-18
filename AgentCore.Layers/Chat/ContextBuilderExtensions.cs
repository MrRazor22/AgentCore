using AgentCore;
using AgentCore.Context;
using AgentCore.Layers.Context.Store;

namespace AgentCore.Layers.Context;

public static class ContextExtensions
{
    public static IContext WithPersistence(
        this IContext context,
        IChatStore store,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        return new ChatPersistenceLayer(store, walStore, context);
    }

    public static IContext WithPersistence(
        this IContext context,
        string storageDirectory,
        string sessionId,
        bool enableWal = true)
        => context.WithPersistence(
            new FileChatStore(storageDirectory, sessionId),
            enableWal ? new FileWalStore(storageDirectory, sessionId) : null);

    public static Agent WithPersistence(
        this Agent agent,
        IChatStore store,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.WithContext(agent.Context.WithPersistence(store, walStore));
    }

    public static Agent WithPersistence(
        this Agent agent,
        string storageDirectory,
        string sessionId,
        bool enableWal = true)
        => agent.WithPersistence(
            new FileChatStore(storageDirectory, sessionId),
            enableWal ? new FileWalStore(storageDirectory, sessionId) : null);
}
