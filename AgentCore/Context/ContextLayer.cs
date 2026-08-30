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

    public virtual Task<IReadOnlyList<Message>> GetMessagesAsync(
        CancellationToken ct = default)
        => Inner.GetMessagesAsync(ct);

    public virtual Task AddAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default)
        => Inner.AddAsync(messages, ct);
}
