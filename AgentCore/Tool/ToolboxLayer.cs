using AgentCore.LLM.Chat;

namespace AgentCore.Tool;

public delegate IAsyncEnumerable<IMessageEvent> ToolboxDelegate(
    IReadOnlyList<ToolCall> calls,
    IToolbox next,
    CancellationToken ct);

public class ToolboxLayer(IToolbox? inner = null, ToolboxDelegate? handler = null) : IToolbox, ILayer<IToolbox>
{
    public IToolbox Inner { get; private set; } = inner!;

    public void Attach(IToolbox inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
        => Inner != null ? Inner.GetToolsAsync(ct) : new(Array.Empty<ITool>());

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        CancellationToken ct = default)
        => handler != null
            ? handler(calls, Inner, ct)
            : Inner.ExecuteAsync(calls, ct);
}
