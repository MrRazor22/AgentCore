using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public interface IContext
{
    IReadOnlyList<Message> Messages { get; }
    Task AddAsync(Message message, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Message> messages, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}


