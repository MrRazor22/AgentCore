using AgentCore.Layers.Chat;

namespace AgentCore;

public static class ChatPersistenceBuilderExtensions
{
    public static Agent.Builder AddChatPersistence(
        this Agent.Builder builder,
        IChatStore store,
        string sessionId,
        bool autoRestore = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddContextLayer(new ChatPersistenceLayer(store, sessionId, autoRestore));
    }
}
