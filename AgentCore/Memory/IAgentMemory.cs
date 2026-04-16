using AgentCore.Conversation;

namespace AgentCore.Memory;

/// <summary>
/// Core memory interface. Simple contract for memory systems.
/// Implementations can be simple (CoreMemory) or advanced (MemoryEngine with AMFS).
/// </summary>
public interface IAgentMemory
{
    /// <summary>
    /// Retrieves memories for injection into prompt.
    /// Simple implementations return all blocks. Advanced implementations do semantic search.
    /// </summary>
    Task<IReadOnlyList<IContent>> RecallAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Stores new information from the conversation.
    /// Simple implementations append to scratchpad. Advanced implementations store with embeddings.
    /// </summary>
    Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);
}
