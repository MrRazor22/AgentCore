using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.LLM;

public interface ILLM
{
    IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default);
}
