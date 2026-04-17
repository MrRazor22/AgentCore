namespace AgentCore.Memory;

/// <summary>
/// Semantic memory operations - extends IAgentMemory with semantic search and AMFS features.
/// Implemented by MemoryEngine in AgentCore.Memory.
/// </summary>
public interface ISemanticMemory : IAgentMemory
{
    /// <summary>Deep multi-step synthesis over all stored memories.</summary>
    Task<string> ReflectAsync(string query, CancellationToken ct = default);

    /// <summary>Outcome feedback for AMFS confidence adjustment.</summary>
    Task CommitOutcomeAsync(OutcomeType outcome, CancellationToken ct = default);
}
