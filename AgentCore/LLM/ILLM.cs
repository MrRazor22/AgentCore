using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;
using AgentCore.Tooling.Tools;

namespace AgentCore.LLM;

public interface ILLM
{
    IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default);
}

