using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public interface IContext
{
    Task StageAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default);

    Task<IReadOnlyList<Message>> PreparePromptAsync(
        CancellationToken ct = default);

    Task CommitAsync(
        IReadOnlyList<Message> response,
        TokenUsage? usage = null,
        CancellationToken ct = default);
}
