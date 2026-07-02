using AgentCore.Conversation;
using AgentCore.Tokens;

namespace AgentCore.Memory;

public interface IMemory
{
    Task RememberAsync(
        string sessionId,
        IReadOnlyList<Message> completedTurn,
        CancellationToken ct = default);

    Task<IReadOnlyList<Message>> RecallAsync(
        string sessionId,
        Message currentInput,
        TokenBudget budget,
        CancellationToken ct = default);

    Task ClearAsync(
        string sessionId,
        CancellationToken ct = default);
}
