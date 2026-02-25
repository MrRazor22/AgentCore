using AgentCore.Chat;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace AgentCore.Providers;

public interface ILLMProvider
{
    (IAsyncEnumerable<IContentDelta> Content, Task<LLMMeta> Meta) StreamAsync(
        IReadOnlyList<Message> messages, 
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default);
}
