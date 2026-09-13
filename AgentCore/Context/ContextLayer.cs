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

    public virtual Task<IReadOnlyList<Message>> GetAsync(
        CancellationToken ct = default)
        => Inner.GetAsync(ct);

    public virtual Task AppendAsync(
        IMessageEvent evt,
        CancellationToken ct = default)
        => Inner.AppendAsync(evt, ct);

    public virtual Task AppendAsync(
        Message message,
        CancellationToken ct = default)
        => Inner.AppendAsync(message, ct);
}
