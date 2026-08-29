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

    public virtual Task StageAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default)
        => Inner.StageAsync(messages, ct);

    public virtual Task<IReadOnlyList<Message>> PreparePromptAsync(
        CancellationToken ct = default)
        => Inner.PreparePromptAsync(ct);

    public virtual Task CommitAsync(
        Message response,
        CancellationToken ct = default)
        => CommitAsync([response], ct);

    public virtual Task CommitAsync(
        IReadOnlyList<Message> response,
        CancellationToken ct = default)
        => Inner.CommitAsync(response, ct);
}
