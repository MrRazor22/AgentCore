using AgentCore;
using AgentCore.Context;

namespace AgentCore.Layers.Chat;

public static class ChatPersistenceBuilderExtensions
{
    public static ContextBuilder AddChatPersistence(
        this ContextBuilder builder,
        IChatStore store,
        string sessionId,
        bool autoRestore = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ChatPersistenceLayer(store, sessionId, autoRestore));
    }

    public static AgentBuilder AddChatPersistence(
        this AgentBuilder builder,
        IChatStore store,
        string sessionId,
        bool autoRestore = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseContext(ctx => ctx.AddChatPersistence(store, sessionId, autoRestore));
    }
}
