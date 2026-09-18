using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public static class AgentExtensions
{
    public static T? FindLayer<T>(this object? root) where T : class
    {
        for (var c = root; c != null; c = GetInner(c))
            if (c is T match) return match;
        return null;

        static object? GetInner(object obj) => obj switch
        {
            ContextLayer contextLayer => contextLayer.Inner,
            LLMLayer llmLayer => llmLayer.Inner,
            ToolingLayer toolingLayer => toolingLayer.Inner,
            _ => null
        };
    }

    public static async Task<string?> GetFinalResponseAsync(
        this IAsyncEnumerable<IContentEvent> stream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        string? text = null;

        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
            if (evt is Text t) text = t.Value;

        return text;
    }

    public static IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        this IAgent agent,
        string prompt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(prompt);
        return agent.InvokeStreamingAsync([new Text(prompt)], ct);
    }

    public static IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        this IAgent agent,
        IContent input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(input);
        return agent.InvokeStreamingAsync([input], ct);
    }

    public static IContext WithoutLayer<T>(this IContext context) where T : class
    {
        if (context is T layer && layer is ContextLayer cl)
            return cl.Inner.WithoutLayer<T>();

        if (context is ContextLayer parent)
        {
            var newInner = parent.Inner.WithoutLayer<T>();
            if (!ReferenceEquals(newInner, parent.Inner))
                parent.Attach(newInner);
        }
        return context;
    }

    public static ILLM WithoutLayer<T>(this ILLM llm) where T : class
    {
        if (llm is T layer && layer is LLMLayer ll)
            return ll.Inner.WithoutLayer<T>();

        if (llm is LLMLayer parent)
        {
            var newInner = parent.Inner.WithoutLayer<T>();
            if (!ReferenceEquals(newInner, parent.Inner))
                parent.Attach(newInner);
        }
        return llm;
    }

    public static ITooling WithoutLayer<T>(this ITooling tooling) where T : class
    {
        if (tooling is T layer && layer is ToolingLayer tl)
            return tl.Inner.WithoutLayer<T>();

        if (tooling is ToolingLayer parent)
        {
            var newInner = parent.Inner.WithoutLayer<T>();
            if (!ReferenceEquals(newInner, parent.Inner))
                parent.Attach(newInner);
        }
        return tooling;
    }

    public static Agent WithoutLayer<T>(this Agent agent) where T : class
        => agent
            .UseContext(agent.Context.WithoutLayer<T>())
            .UseLLM(agent.LLM.WithoutLayer<T>())
            .UseTooling(agent.Tooling.WithoutLayer<T>());

    public static Agent RemoveLayer<T>(this Agent agent) where T : class
        => agent.WithoutLayer<T>();

    public static Agent AddTools<T>(this Agent agent) => agent.AddTools(typeof(T));

    public static Agent AddTools(this Agent agent, object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance as Type ?? instance.GetType();
        var target = instance is Type ? null : instance;
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | (target != null ? System.Reflection.BindingFlags.Instance : 0);

        var extracted = type.GetMethods(flags)
            .Where(m => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<ToolAttribute>(m) != null)
            .Select(m => (ITool)new Tool.Tools.MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return agent.AddTools([.. extracted]);
    }

    public static Agent WithLLM(this Agent a, ILLM l) => a.UseLLM(l);
    public static Agent WithContext(this Agent a, IContext c) => a.UseContext(c);
    public static Agent WithTooling(this Agent a, ITooling t) => a.UseTooling(t);
    public static Agent WithInstructions(this Agent a, IEnumerable<IContent> i) => a.UseInstructions(i.ToArray());
    public static Agent WithEngine(this Agent a, IAgentEngine e) => a.UseEngine(e);
    public static Agent WithTools(this Agent a, params ITool[] t) => a.AddTools(t);
    public static Agent WithTools(this Agent a, IEnumerable<ITool> t) => a.AddTools(t);
    public static Agent WithTools<T>(this Agent a) => a.AddTools<T>();
    public static Agent WithTools(this Agent a, object instance, params IMetadata[] meta) => a.AddTools(instance, meta);
    public static Agent WithoutTools(this Agent a, params string[] names) => names.Aggregate(a, (cur, n) => cur.RemoveTool(n));
    public static Agent WithoutTools(this Agent a, Func<ITool, bool> predicate) => a.RemoveTools(predicate);
    public static Agent WithLayer(this Agent a, ContextLayer l) => a.AddLayer(l);
    public static Agent WithLayer(this Agent a, LLMLayer l) => a.AddLayer(l);
    public static Agent WithLayer(this Agent a, ToolingLayer l) => a.AddLayer(l);
}
