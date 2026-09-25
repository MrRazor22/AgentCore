using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.LLM;

public class LLMLayer(ILLM? inner = null) : ILLM, ILayer<ILLM>
{
    public ILLM Inner { get; private set; } = inner!;

    public void Attach(ILLM inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
        => Inner.GenerateAsync(messages, tools, responseSchema, ct);
}
