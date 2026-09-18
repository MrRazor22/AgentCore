using AgentCore.LLM.Chat;

namespace AgentCore.Tool;

public delegate IAsyncEnumerable<IMessageEvent> ToolingDelegate(
    IReadOnlyList<ToolCall> calls,
    IReadOnlyList<ITool> tools,
    ITooling next,
    CancellationToken ct);

public class ToolingLayer(ToolingDelegate? handler = null) : ITooling
{
    private bool _attached;

    public ITooling Inner { get; private set; } = null!;

    internal void Attach(ITooling inner)
    {
        if (_attached)
            throw new InvalidOperationException("This tool service decorator has already been attached to a pipeline.");

        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _attached = true;
    }

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default)
        => handler != null
            ? handler(calls, tools, Inner, ct)
            : Inner.ExecuteAsync(calls, tools, ct);
}
