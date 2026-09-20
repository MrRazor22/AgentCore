using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public class ContextLayer(IContext? inner = null) : IContext, ILayer<IContext>
{
    public IContext Inner { get; private set; } = inner!;

    public void Attach(IContext inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
        => Inner.ReadAsync(ct);

    public virtual IAsyncEnumerable<IContentEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        CancellationToken ct = default)
        => Inner.WriteAsync(events, ct);
}
