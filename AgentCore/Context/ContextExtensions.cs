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

    public static IContext Attach(this IContext context, IContext inner)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inner);
        if (context is ContextLayer cl) cl.Attach(inner);
        return context;
    }

    public static IContext AddLayer(this IContext context, ContextLayer layer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(layer);
        if (context is ContextLayer cl) return cl.AddLayer(layer);
        layer.Attach(context);
        return layer;
    }

    public static IContext RemoveLayer<T>(this IContext context) where T : class
        => context is ContextLayer cl ? cl.RemoveLayer<T>() : context;
}
