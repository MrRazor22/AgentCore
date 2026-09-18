using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public static class ContextExtensions
{
    public static async Task WriteAsync(this IContext context, IMessageEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evt);
        await foreach (var _ in context.WriteAsync(Stream(evt), ct).ConfigureAwait(false)) { }
        static async IAsyncEnumerable<IMessageEvent> Stream(IMessageEvent e) { yield return e; }
    }

    public static Agent AddLayer(this Agent agent, ContextLayer layer)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(agent.Context);
        return agent.With(context: layer);
    }

    public static IContext RemoveLayer<T>(this IContext context) where T : class
    {
        if (context is T layer && layer is ContextLayer cl)
            return cl.Inner.RemoveLayer<T>();

        if (context is ContextLayer parent)
        {
            var newInner = parent.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, parent.Inner))
                parent.Attach(newInner);
        }
        return context;
    }

    public static Agent RemoveLayer<T>(this Agent agent) where T : ContextLayer
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.With(context: agent.Context.RemoveLayer<T>());
    }
}
