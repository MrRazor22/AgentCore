using AgentCore;
using AgentCore.Context;

namespace AgentCore.Layers.Chat;

public static class ChatPersistenceBuilderExtensions
{
    public static ContextBuilder AddChatPersistence(
        this ContextBuilder builder,
        IChatStore store,
        string sessionId,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ChatPersistenceLayer(store, sessionId, walStore));
    }

    public static ContextBuilder AddChatPersistence(
        this ContextBuilder builder,
        string storageDirectory,
        string sessionId,
        IWalStore? walStore = null)
        => builder.AddChatPersistence(new FileChatStore(storageDirectory), sessionId, walStore);

    public static AgentBuilder AddChatPersistence(
        this AgentBuilder builder,
        IChatStore store,
        string sessionId,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseContext(ctx => ctx.AddChatPersistence(store, sessionId, walStore));
    }

    public static AgentBuilder AddChatPersistence(
        this AgentBuilder builder,
        string storageDirectory,
        string sessionId,
        IWalStore? walStore = null)
        => builder.AddChatPersistence(new FileChatStore(storageDirectory), sessionId, walStore);
}
