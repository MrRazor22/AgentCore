using AgentCore.LLM.Chat;

namespace AgentCore.Tool;

public delegate IAsyncEnumerable<IMessageEvent> ToolingDelegate(
    IReadOnlyList<ToolCall> calls,
    IReadOnlyList<ITool> tools,
    ITooling next,
    CancellationToken ct);

public class ToolingLayer : ITooling
{
    private readonly ToolingDelegate? _handler;

    public ToolingLayer(ToolingDelegate? handler = null) => _handler = handler;

    public ToolingLayer(ITooling inner, ToolingDelegate? handler = null)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _handler = handler;
    }

    public ITooling Inner { get; private set; } = null!;

    public void Attach(ITooling inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default)
        => _handler != null
            ? _handler(calls, tools, Inner, ct)
            : Inner.ExecuteAsync(calls, tools, ct);
}
