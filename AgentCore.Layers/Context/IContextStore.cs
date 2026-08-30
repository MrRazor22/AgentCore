using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context;

/// <summary>
/// Defines a contract for loading and saving conversation message snapshots keyed by session ID.
/// Implemented by application or infrastructure storage providers (file, SQLite, Redis, etc.).
/// </summary>
public interface IContextStore
{
    Task<IReadOnlyList<Message>?> LoadAsync(string sessionId, CancellationToken ct = default);
    Task SaveAsync(string sessionId, IReadOnlyList<Message> messages, CancellationToken ct = default);
}
