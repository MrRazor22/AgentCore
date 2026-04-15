using AgentCore.Conversation;

namespace AgentCore;

/// <summary>
/// The agent's cognitive memory — a world model that persists knowledge across turns and sessions.
/// Provides query-driven retrieval, automatic retention, deep synthesis, and outcome-based confidence tuning.
/// </summary>
public interface IAgentMemory
{
    /// <summary>
    /// Called BEFORE each LLM step. Semantically retrieves relevant knowledge for the given query.
    /// Returns content ready to inject into the context window.
    /// </summary>
    Task<IReadOnlyList<IContent>> RecallAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Called AFTER each completed turn. Extracts and stores facts, experiences, and beliefs
    /// from the conversation messages. Fire-and-forget friendly (non-fatal).
    /// </summary>
    Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);

    /// <summary>
    /// Deep multi-step synthesis over all stored memories. Creates or updates an Observation entry.
    /// Called by LLM via MemoryTools or directly by developer code.
    /// </summary>
    Task<string> ReflectAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Outcome feedback. Adjusts confidence of recently recalled entries based on task success/failure.
    /// Called by LLM via MemoryTools or directly by developer code after a task completes.
    /// </summary>
    Task CommitOutcomeAsync(OutcomeType outcome, CancellationToken ct = default);
}

/// <summary>
/// Outcome types for memory confidence adjustment (AMFS pattern).
/// Success → boost confidence; Failure → reduce confidence; CriticalFailure → mark for pruning.
/// </summary>
public enum OutcomeType
{
    Success,
    MinorFailure,
    Failure,
    CriticalFailure
}
