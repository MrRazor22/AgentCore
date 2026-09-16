using AgentCore.Context;
using AgentCore.Layers.Context.Store;

namespace AgentCore.Layers.Context;

public static class ContextBuilderExtensions
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

    public static ContextBuilder AddChatGrammar(
        this ContextBuilder builder,
        bool coalesceAdjacentRoles = true,
        bool ensureToolPairing = true,
        bool pinSystemInstructions = true,
        bool stripPastReasoning = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ChatGrammarLayer(
            coalesceAdjacentRoles,
            ensureToolPairing,
            pinSystemInstructions,
            stripPastReasoning));
    }
}
