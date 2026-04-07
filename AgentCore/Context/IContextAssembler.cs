using AgentCore.Conversation;

namespace AgentCore.Context;

/// <summary>
/// Assembles registered context sources into messages for the LLM, 
/// respecting token budgets and priorities.
/// </summary>
public interface IContextAssembler
{
    /// <summary>Register a context source with optional token budget.</summary>
    void Register(IContextSource source, int? maxTokenBudget = null);
    
    /// <summary>
    /// Assembles all registered sources into messages, fitting within availableTokens.
    /// </summary>
    Task<IReadOnlyList<Message>> AssembleAsync(int availableTokens, CancellationToken ct = default);
}
