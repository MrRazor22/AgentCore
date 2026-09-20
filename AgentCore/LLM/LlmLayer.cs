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

public class LLMLayer(ILLM? inner = null, LLMDelegate? handler = null) : ILLM
{
    public ILLM Inner { get; private set; } = inner!;

    public void Attach(ILLM inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public LLMLayer AddLayer(LLMLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(Inner);
        Inner = layer;
        return this;
    }

    public ILLM RemoveLayer<T>() where T : class
    {
        if (this is T) return Inner is LLMLayer ml ? ml.RemoveLayer<T>() : Inner;
        if (Inner is LLMLayer ml) Inner = ml.RemoveLayer<T>();
        return this;
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
