using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public interface IContext
{
    Task<IReadOnlyList<Message>> GetMessagesAsync(
        CancellationToken ct = default);

    Task AddAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default);
}
