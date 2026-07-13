using AgentCore.Conversation;
using AgentCore.Schema;
using AgentCore.Tools;

namespace AgentCore.LLM;

public interface ILLMProvider
{
    int ContextWindow { get; }

    IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default);
}
