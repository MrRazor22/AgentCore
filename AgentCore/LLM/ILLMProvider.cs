using AgentCore.Chat;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace AgentCore.LLM;

public interface ILLMProvider
{
    IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default);
}
