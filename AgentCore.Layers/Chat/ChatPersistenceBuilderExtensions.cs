using AgentCore;

namespace AgentCore.Layers.Chat;

public static class ChatPersistenceBuilderExtensions
{
    public static AgentBuilder AddChatPersistence(
        this AgentBuilder builder,
        IChatStore store,
        string sessionId,
        bool autoRestore = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddContextLayer(new ChatPersistenceLayer(store, sessionId, autoRestore));
    }
}
