using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public abstract class ContextLayer : IContext
{
    private bool _attached;

    /// <summary>
    /// Gets the inner memory layer.
    /// </summary>
    public IContext Inner { get; private set; } = null!;

    internal void Attach(IContext inner)
    {
        if (_attached)
            throw new InvalidOperationException("This memory decorator has already been attached to a pipeline.");

        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _attached = true;
    }

    public virtual Task<IReadOnlyList<Message>> BuildPromptAsync(
        IReadOnlyList<Message> uncommittedMessages,
        CancellationToken ct = default)
        => Inner.BuildPromptAsync(uncommittedMessages, ct);

    public virtual Task CommitAsync(
        TokenUsage usage,
        IReadOnlyList<Message> response,
        CancellationToken ct = default)
        => Inner.CommitAsync(usage, response, ct);
}
