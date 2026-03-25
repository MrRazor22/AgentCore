using AgentCore.Conversation;

namespace AgentCore.Tokens;

public interface ITokenCounter
{
    Task<int> CountAsync(IEnumerable<Message> messages, CancellationToken ct = default);
}
