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
