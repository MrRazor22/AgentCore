using AgentCore.Conversation;

namespace AgentCore.Memory;

/// <summary>
/// Memory interface for agent cognitive systems.
/// Implementations provide semantic search and knowledge storage (e.g., MemoryEngine with AMFS).
/// Memory is optional - agents can function without it.
/// </summary>
public interface IAgentMemory
{
    /// <summary>
    /// Retrieves memories for injection into prompt.
    /// Advanced implementations do semantic search.
    /// </summary>
    Task<IReadOnlyList<IContent>> RecallAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Stores new information from the conversation.
    /// Advanced implementations store with embeddings and extraction.
    /// </summary>
    Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);
}
