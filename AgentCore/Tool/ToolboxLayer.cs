using AgentCore.LLM.Chat;

namespace AgentCore.Tool;

public delegate IAsyncEnumerable<IMessageEvent> ToolboxDelegate(
    IReadOnlyList<ToolCall> calls,
    IToolbox next,
    CancellationToken ct);

public class ToolboxLayer(IToolbox? inner = null, ToolboxDelegate? handler = null) : IToolbox
{
    public IToolbox Inner { get; private set; } = inner!;

    public void Attach(IToolbox inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public ToolboxLayer AddLayer(ToolboxLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(Inner);
        Inner = layer;
        return this;
    }

    public IToolbox RemoveLayer<T>() where T : class
    {
        if (this is T) return Inner is ToolboxLayer tl ? tl.RemoveLayer<T>() : Inner;
        if (Inner is ToolboxLayer tl) Inner = tl.RemoveLayer<T>();
        return this;
    }

    public virtual ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
        => Inner != null ? Inner.GetToolsAsync(ct) : new(Array.Empty<ITool>());

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        CancellationToken ct = default)
        => handler != null
            ? handler(calls, Inner, ct)
            : Inner.ExecuteAsync(calls, ct);
}
