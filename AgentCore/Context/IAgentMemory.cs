using AgentCore.Conversation;

namespace AgentCore.Memory;

public interface IAgentMemory
{
    Task<IReadOnlyList<Message>> RecallAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);
    Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);
}
