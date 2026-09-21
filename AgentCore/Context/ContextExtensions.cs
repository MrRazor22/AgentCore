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
        if (context is ILayer<IContext> layer) layer.Attach(inner);
        return context;
    }

    public static IContext AddLayer(this IContext context, IContext layer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(layer);
        if (context is ILayer<IContext> head && layer is ILayer<IContext> next)
        {
            next.Attach(head.Inner);
            head.Attach(layer);
            return context;
        }
        if (layer is ILayer<IContext> l) l.Attach(context);
        return layer;
    }

    public static IContext RemoveLayer<T>(this IContext context) where T : class
    {
        if (context is T && context is ILayer<IContext> self) return self.Inner.RemoveLayer<T>();
        if (context is ILayer<IContext> head)
        {
            var newInner = head.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, head.Inner)) head.Attach(newInner);
        }
        return context;
    }

    public static TL? FindLayer<TL>(this IContext root) where TL : class
    {
        for (var c = root; c != null; c = (c as ILayer<IContext>)?.Inner)
            if (c is TL match) return match;
        return null;
    }

    public static IReadOnlyList<Message> Snapshot(this IReadOnlyList<Message> messages, string? upToMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (upToMessageId == null) return messages;
        for (int i = 0; i < messages.Count; i++)
            if (string.Equals(messages[i].Id, upToMessageId, StringComparison.Ordinal))
                return messages.Take(i + 1).ToList();
        return messages;
    }
}
