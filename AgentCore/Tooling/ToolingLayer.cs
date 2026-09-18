using AgentCore.LLM.Chat;

namespace AgentCore.Tooling;

public delegate IAsyncEnumerable<IMessageEvent> ToolingDelegate(
    IReadOnlyList<ToolCall> calls,
    IToolbox next,
    CancellationToken ct);

public class ToolingLayer(ToolingDelegate? handler = null) : IToolbox
{
    private bool _attached;

    public IToolbox Inner { get; private set; } = null!;

    internal void Attach(IToolbox inner)
    {
        if (_attached)
            throw new InvalidOperationException("This tool service decorator has already been attached to a pipeline.");

        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _attached = true;
    }

    public virtual IReadOnlyList<ToolDefinition> GetDefinitions() => Inner.GetDefinitions();

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        CancellationToken ct = default)
        => handler != null
            ? handler(calls, Inner, ct)
            : Inner.ExecuteAsync(calls, ct);
}
