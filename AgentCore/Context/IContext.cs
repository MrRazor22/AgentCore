using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public interface IContext
{
    Task<IReadOnlyList<Message>> BuildPromptAsync(
        IReadOnlyList<Message> promptInput,
        CancellationToken ct = default);

    Task CommitAsync(
        TokenUsage usage,
        IReadOnlyList<Message> response,
        CancellationToken ct = default);
}
