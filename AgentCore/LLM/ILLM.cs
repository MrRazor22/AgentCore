using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.LLM;

public interface ILLM
{
    IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default);
}

