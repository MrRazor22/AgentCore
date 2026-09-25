using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public static class AgentExtensions
{
    public static Agent With(
        this IAgent agent,
        Func<ILLM, ILLM>? llm = null, Func<IToolbox, IToolbox>? toolbox = null, Func<IContext, IContext>? context = null,
        IReadOnlyList<IContent>? instructions = null, int? maxIterations = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new(
            llm != null ? _ => llm(agent.LLM) : _ => agent.LLM,
            toolbox != null ? _ => toolbox(agent.Toolbox) : _ => agent.Toolbox,
            context != null ? _ => context(agent.Context) : _ => agent.Context,
            instructions ?? agent.Instructions,
            maxIterations ?? (agent as Agent)?.MaxIterations ?? 20);
    }

    public static T Add<T>(this T pipeline, T layer) where T : class
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(layer);
        if (pipeline is ILayer<T> head && layer is ILayer<T> next) { next.Attach(head.Inner); head.Attach(layer); return pipeline; }
        if (layer is ILayer<T> l) l.Attach(pipeline);
        return layer;
    }

    public static T AddBefore<T, TTarget>(this T pipeline, T layer) where T : class where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(layer);
        if (pipeline is TTarget) { if (layer is ILayer<T> l) l.Attach(pipeline); return layer; }
        if (pipeline is ILayer<T> head) head.Attach(head.Inner.AddBefore<T, TTarget>(layer));
        return pipeline;
    }

    public static T AddAfter<T, TTarget>(this T pipeline, T layer) where T : class where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(layer);
        for (var curr = pipeline; curr != null; curr = curr is ILayer<T> l ? l.Inner : null)
        {
            if (curr is TTarget && curr is ILayer<T> match && layer is ILayer<T> next)
            {
                next.Attach(match.Inner);
                match.Attach(layer);
                break;
            }
        }
        return pipeline;
    }

    public static T Remove<T, TLayer>(this T pipeline) where T : class where TLayer : class
    {
        if (pipeline is TLayer && pipeline is ILayer<T> self) return self.Inner.Remove<T, TLayer>();
        if (pipeline is ILayer<T> head)
        {
            var newInner = head.Inner.Remove<T, TLayer>();
            if (!ReferenceEquals(newInner, head.Inner)) head.Attach(newInner);
        }
        return pipeline;
    }

    public static T Replace<T, TTarget>(this T pipeline, T replacement) where T : class where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(replacement);
        if (pipeline is TTarget && pipeline is ILayer<T> self && replacement is ILayer<T> head)
        {
            head.Attach(self.Inner);
            return replacement;
        }
        for (var curr = pipeline; curr != null; curr = curr is ILayer<T> l ? l.Inner : null)
        {
            if (curr is ILayer<T> parent && parent.Inner is TTarget && parent.Inner is ILayer<T> target && replacement is ILayer<T> next)
            {
                next.Attach(target.Inner);
                parent.Attach(replacement);
                break;
            }
        }
        return pipeline;
    }

    public static ILLM AddBefore<TTarget>(this ILLM llm, ILLM layer) where TTarget : class => llm.AddBefore<ILLM, TTarget>(layer);
    public static IToolbox AddBefore<TTarget>(this IToolbox t, IToolbox layer) where TTarget : class => t.AddBefore<IToolbox, TTarget>(layer);
    public static IContext AddBefore<TTarget>(this IContext c, IContext layer) where TTarget : class => c.AddBefore<IContext, TTarget>(layer);

    public static ILLM AddAfter<TTarget>(this ILLM llm, ILLM layer) where TTarget : class => llm.AddAfter<ILLM, TTarget>(layer);
    public static IToolbox AddAfter<TTarget>(this IToolbox t, IToolbox layer) where TTarget : class => t.AddAfter<IToolbox, TTarget>(layer);
    public static IContext AddAfter<TTarget>(this IContext c, IContext layer) where TTarget : class => c.AddAfter<IContext, TTarget>(layer);

    public static ILLM Replace<TTarget>(this ILLM llm, ILLM next) where TTarget : class => llm.Replace<ILLM, TTarget>(next);
    public static IToolbox Replace<TTarget>(this IToolbox t, IToolbox next) where TTarget : class => t.Replace<IToolbox, TTarget>(next);
    public static IContext Replace<TTarget>(this IContext c, IContext next) where TTarget : class => c.Replace<IContext, TTarget>(next);

    public static ILLM Remove<TTarget>(this ILLM llm) where TTarget : class => llm.Remove<ILLM, TTarget>();
    public static IToolbox Remove<TTarget>(this IToolbox t) where TTarget : class => t.Remove<IToolbox, TTarget>();
    public static IContext Remove<TTarget>(this IContext c) where TTarget : class => c.Remove<IContext, TTarget>();

    public static async Task<string?> GetFinalResponseAsync(this IAsyncEnumerable<IContentEvent> stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        string? text = null;
        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
            if (evt is Text t) text = t.Value;
        return text;
    }
}

public interface ILayer<T> where T : class
{
    T Inner { get; }
    void Attach(T inner);
}
