using AgentCore;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

/// <summary>
/// Contract for loading and appending full conversation message history keyed by session ID.
/// Keeps storage dumb and unaware of compaction/working-context representations.
/// </summary>
public interface IChatStore
{
    Task<IReadOnlyList<Message>?> LoadAsync(string sessionId, CancellationToken ct = default);
    Task AppendAsync(string sessionId, IReadOnlyList<Message> messages, CancellationToken ct = default);
}

/// <summary>
/// Optional contract for append-only streaming event logging and crash recovery.
/// </summary>
public interface IWalStore
{
    Task AppendAsync(string sessionId, IMessageEvent evt, CancellationToken ct = default);
    Task ClearAsync(string sessionId, CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> RecoverAsync(string sessionId, CancellationToken ct = default);
}
