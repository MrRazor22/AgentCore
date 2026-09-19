using AgentCore;
using AgentCore.Context;
using AgentCore.Layers.Context.Store;

namespace AgentCore.Layers.Context;

public static class SessionExtensions
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

    public static IContext RemoveSession(this IContext context)
        => context.RemoveLayer<ChatPersistenceLayer>();
}
