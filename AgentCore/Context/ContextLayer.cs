using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public abstract class ContextLayer : IContext
{
    /// <summary>
    /// Gets the inner memory layer.
    /// </summary>
    public IContext Inner { get; }

    protected ContextLayer(IContext inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
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
