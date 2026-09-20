using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public class ContextLayer(IContext? inner = null) : IContext
{
    public IContext Inner { get; private set; } = inner!;

    public void Attach(IContext inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public ContextLayer AddLayer(ContextLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(Inner);
        Inner = layer;
        return this;
    }

    public IContext RemoveLayer<T>() where T : class
    {
        if (this is T) return Inner is ContextLayer cl ? cl.RemoveLayer<T>() : Inner;
        if (Inner is ContextLayer cl) Inner = cl.RemoveLayer<T>();
        return this;
    }

    public virtual Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
        => Inner.ReadAsync(ct);

    public virtual IAsyncEnumerable<IContentEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        CancellationToken ct = default)
        => Inner.WriteAsync(events, ct);
}
