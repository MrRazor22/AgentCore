using AgentCore.LLM;
using AgentCore.LLM.Conversation;

namespace AgentCore.Memory;

public interface IMemoryService
{
    Task RememberAsync(
        IReadOnlyList<Message> completedTurn,
        CancellationToken ct = default);

    Task<IReadOnlyList<Message>> RecallAsync(
        Message currentInput,
        int? maxTokens,
        CancellationToken ct = default);
}

