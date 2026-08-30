using AgentCore.Context;
using AgentCore.Layers.Context;

namespace AgentCore;

public static class ContextPersistenceBuilderExtensions
{
    public static Agent.Builder AddContextPersistence(
        this Agent.Builder builder,
        IContextStore store,
        string sessionId,
        bool autoRestore = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddContextLayer(new ContextPersistenceLayer(store, sessionId, autoRestore));
    }
}
