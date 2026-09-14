using AgentCore;
using AgentCore.Context;

namespace AgentCore.Layers.Chat;

public static class ChatPersistenceBuilderExtensions
{
    public static ContextBuilder AddChatPersistence(
        this ContextBuilder builder,
        IChatStore store,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ChatPersistenceLayer(store, walStore));
    }

    public static ContextBuilder AddChatPersistence(
        this ContextBuilder builder,
        string storageDirectory,
        string sessionId,
        bool enableWal = true)
        => builder.AddChatPersistence(
            new FileChatStore(storageDirectory, sessionId),
            enableWal ? new FileWalStore(storageDirectory, sessionId) : null);

    public static AgentBuilder AddChatPersistence(
        this AgentBuilder builder,
        IChatStore store,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseContext(ctx => ctx.AddChatPersistence(store, walStore));
    }

    public static AgentBuilder AddChatPersistence(
        this AgentBuilder builder,
        string storageDirectory,
        string sessionId,
        bool enableWal = true)
        => builder.AddChatPersistence(
            new FileChatStore(storageDirectory, sessionId),
            enableWal ? new FileWalStore(storageDirectory, sessionId) : null);
}
