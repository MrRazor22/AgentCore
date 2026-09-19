using AgentCore.LLM.Chat;

namespace AgentCore.Tool;

public delegate IAsyncEnumerable<IMessageEvent> ToolingDelegate(
    IReadOnlyList<ToolCall> calls,
    ITooling next,
    CancellationToken ct);

public class ToolingLayer(ITooling? inner = null, ToolingDelegate? handler = null) : ITooling
{
    public ITooling Inner { get; private set; } = inner!;

    public void Attach(ITooling inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual ValueTask<IReadOnlyList<ToolDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
        => Inner != null ? Inner.GetDefinitionsAsync(ct) : new(Array.Empty<ToolDefinition>());

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        CancellationToken ct = default)
        => handler != null
            ? handler(calls, Inner, ct)
            : Inner.ExecuteAsync(calls, ct);
}
