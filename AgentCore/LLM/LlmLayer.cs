using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.LLM;

public delegate IAsyncEnumerable<IMessageEvent> LLMDelegate(
    IReadOnlyList<Message> messages,
    IReadOnlyList<ToolDefinition>? tools,
    JsonSchema? responseSchema,
    ILLM next,
    CancellationToken ct);

public class LLMLayer(LLMDelegate? handler = null) : ILLM
{
    private bool _attached;

    public ILLM Inner { get; private set; } = null!;

    internal void Attach(ILLM inner)
    {
        if (_attached)
            throw new InvalidOperationException("This LLM decorator has already been attached to a pipeline.");

        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _attached = true;
    }

    public virtual IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
        => handler != null
            ? handler(messages, tools, responseSchema, Inner, ct)
            : Inner.GenerateAsync(messages, tools, responseSchema, ct);
}
