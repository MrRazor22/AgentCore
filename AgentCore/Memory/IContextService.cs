using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Memory;

public interface IContextService
{
    /// <summary>
    /// Prepares the conversation history by calculating the token budget, recalling relevant memory,
    /// injecting memory context, and managing chat history overflow (trimming).
    /// </summary>
    Task<List<Message>> PrepareConversationAsync(
        IContent? instructions,
        Message userInput,
        IReadOnlyList<Tool> tools,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the working conversation history with the completed turn messages.
    /// </summary>
    Task UpdateHistoryAsync(
        IReadOnlyList<Message> completedTurn,
        CancellationToken ct = default);
}
