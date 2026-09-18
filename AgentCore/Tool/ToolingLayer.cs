using AgentCore.LLM.Chat;

namespace AgentCore.Tool;

public delegate IAsyncEnumerable<IMessageEvent> ToolingDelegate(
    IReadOnlyList<ToolCall> calls,
    IReadOnlyList<ITool> tools,
    ITooling next,
    CancellationToken ct);

public class ToolingLayer(ITooling? inner = null, ToolingDelegate? handler = null) : ITooling
{
    public ITooling Inner { get; private set; } = inner!;

    public void Attach(ITooling inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default)
        => handler != null
            ? handler(calls, tools, Inner, ct)
            : Inner.ExecuteAsync(calls, tools, ct);
}
