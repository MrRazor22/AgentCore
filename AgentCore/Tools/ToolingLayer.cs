using AgentCore.LLM.Chat;

namespace AgentCore.Tools;

public abstract class ToolingLayer : ITooling
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

    public virtual IReadOnlyList<ToolDefinition> GetDefinitions() => Inner.GetDefinitions();

    public virtual Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
        => Inner.ExecuteAsync(call, ct);
}
