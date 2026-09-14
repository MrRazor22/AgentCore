using AgentCore.LLM;
using AgentCore.LLM.Chat;
using System.Runtime.CompilerServices;

namespace AgentCore.Tooling;

public abstract class ToolingLayer : IToolbox
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
        => Inner.ExecuteAsync(calls, ct);
}
