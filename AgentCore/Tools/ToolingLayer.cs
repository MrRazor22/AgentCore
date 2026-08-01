using AgentCore.LLM.Chat;

namespace AgentCore.Tools;

public abstract class ToolingLayer : ITooling
{
    public ITooling Inner { get; }

    protected ToolingLayer(ITooling inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public virtual IReadOnlyList<Tool> Tools => Inner.Tools;

    public virtual Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
        => Inner.ExecuteAsync(calls, ct);
}
