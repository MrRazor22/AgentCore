using AgentCore.Chat;

namespace AgentCore.Tokens;

public interface ITokenCounter
{
    Task<int> CountAsync(IEnumerable<Message> messages, CancellationToken ct = default);
}
