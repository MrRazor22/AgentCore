using AgentCore.Conversation;
using AgentCore.Schema;
using AgentCore.Tooling;

namespace AgentCore.LLM;

public interface ILLMProvider
{
    IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default);
}
