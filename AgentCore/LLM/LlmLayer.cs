using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.LLM;

public abstract class LLMLayer : ILLM
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

    public virtual LLMCapabilities GetCapabilities() => Inner.GetCapabilities();

    public virtual IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default)
        => Inner.StreamAsync(messages, options, tools, ct);
}
