using AgentCore.Conversation;

namespace AgentCore.Context;

/// <summary>
/// Manages fitting the conversation state into the provider's token budget constraints.
/// </summary>
public interface IContextManager
{
    /// <summary>
    /// Fits the given conversation state into the specified token budget.
    /// </summary>
    /// <param name="state">The complete history or state of the conversation.</param>
    /// <param name="tokenBudget">The maximum tokens allowed for the history.</param>
    /// <returns>A list of messages fitted within the budget constraints.</returns>
    IReadOnlyList<Message> Manage(IReadOnlyList<Message> state, int tokenBudget);
}
