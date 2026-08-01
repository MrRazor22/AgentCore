using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.LLM;

public abstract class LLMLayer : ILLM
{
    public ILLM Inner { get; }

    protected LLMLayer(ILLM inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public virtual IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default)
        => Inner.StreamAsync(messages, options, tools, ct);
}
