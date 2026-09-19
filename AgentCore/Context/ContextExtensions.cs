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

    public static IContext AddLayer(this IContext context, ContextLayer layer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(context);
        return layer;
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
}
