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

public class LLMLayer : ILLM
{
    private readonly LLMDelegate? _handler;

    public LLMLayer(LLMDelegate? handler = null) => _handler = handler;

    public LLMLayer(ILLM inner, LLMDelegate? handler = null)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _handler = handler;
    }

    public ILLM Inner { get; private set; } = null!;

    public void Attach(ILLM inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public virtual IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
        => _handler != null
            ? _handler(messages, tools, responseSchema, Inner, ct)
            : Inner.GenerateAsync(messages, tools, responseSchema, ct);
}
