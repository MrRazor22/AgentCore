using AgentCore;
using AgentCore.Context;
using AgentCore.Layers.Context.Store;

namespace AgentCore.Layers.Context;

public static class SessionExtensions
{
    public static Agent UseSession(
        this Agent agent,
        string storageDirectory,
        string sessionId,
        int contextWindow = 50000,
        int reserveTokens = 2500,
        bool enableWal = true)
        => agent.UseSession(
            new FileChatStore(storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory)), sessionId ?? throw new ArgumentNullException(nameof(sessionId))),
            enableWal ? new FileWalStore(storageDirectory, sessionId) : null,
            contextWindow,
            reserveTokens);

    public static Agent UseSession(
        this Agent agent,
        IChatStore store,
        IWalStore? walStore = null,
        int contextWindow = 50000,
        int reserveTokens = 2500)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(store);

        var chatContext = new ChatContext(contextWindow: contextWindow, reserveTokens: reserveTokens);
        return agent.UseContext(new ChatPersistenceLayer(store, walStore, chatContext));
    }

    public static Agent RemoveSession(this Agent agent)
        => agent.RemoveLayer<ChatPersistenceLayer>();

    public static Agent WithSession(
        this Agent agent,
        string storageDirectory,
        string sessionId,
        int contextWindow = 50000,
        int reserveTokens = 2500,
        bool enableWal = true)
        => agent.UseSession(storageDirectory, sessionId, contextWindow, reserveTokens, enableWal);

    public static Agent WithSession(
        this Agent agent,
        IChatStore store,
        IWalStore? walStore = null,
        int contextWindow = 50000,
        int reserveTokens = 2500)
        => agent.UseSession(store, walStore, contextWindow, reserveTokens);

    public static Agent WithoutSession(this Agent agent)
        => agent.RemoveSession();

    public static Agent CreateSession(
        this Agent agent,
        string storageDirectory,
        string sessionId,
        int contextWindow = 50000,
        int reserveTokens = 2500,
        bool enableWal = true)
        => agent.WithSession(storageDirectory, sessionId, contextWindow, reserveTokens, enableWal);

    public static Agent CreateSession(
        this Agent agent,
        IChatStore store,
        IWalStore? walStore = null,
        int contextWindow = 50000,
        int reserveTokens = 2500)
        => agent.WithSession(store, walStore, contextWindow, reserveTokens);
}
