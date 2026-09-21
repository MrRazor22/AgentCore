using Microsoft.Extensions.Logging;

namespace AgentCore.Tool;

public static class ToolboxExtensions
{
    public static async ValueTask<IReadOnlyList<ToolDefinition>> GetDefinitionsAsync(this IToolbox toolbox, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        var tools = await toolbox.GetToolsAsync(ct).ConfigureAwait(false);
        return tools.Select(t => t.Definition).ToArray();
    }

    public static IToolbox AddTool(this IToolbox toolbox, ITool tool)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(tool);
        var target = toolbox.FindLayer<Toolbox>() ?? (toolbox as Toolbox);
        target?.Add([tool]);
        return toolbox;
    }

    public static IToolbox AddTool(this IToolbox toolbox, IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(tools);
        var target = toolbox.FindLayer<Toolbox>() ?? (toolbox as Toolbox);
        target?.Add(tools);
        return toolbox;
    }

    public static IToolbox RemoveTool(this IToolbox toolbox, string name)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(name);
        var target = toolbox.FindLayer<Toolbox>() ?? (toolbox as Toolbox);
        target?.Remove(name);
        return toolbox;
    }

    public static IToolbox Attach(this IToolbox toolbox, IToolbox inner)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(inner);
        if (toolbox is ILayer<IToolbox> layer) layer.Attach(inner);
        return toolbox;
    }

    public static IToolbox AddLayer(this IToolbox toolbox, IToolbox layer)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layer);
        if (toolbox is ILayer<IToolbox> head && layer is ILayer<IToolbox> next)
        {
            next.Attach(head.Inner);
            head.Attach(layer);
            return toolbox;
        }
        if (layer is ILayer<IToolbox> l) l.Attach(toolbox);
        return layer;
    }

    public static IToolbox RemoveLayer<T>(this IToolbox toolbox) where T : class
    {
        if (toolbox is T && toolbox is ILayer<IToolbox> self) return self.Inner.RemoveLayer<T>();
        if (toolbox is ILayer<IToolbox> head)
        {
            var newInner = head.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, head.Inner)) head.Attach(newInner);
        }
        return toolbox;
    }

    public static TL? FindLayer<TL>(this IToolbox root) where TL : class
    {
        for (var c = root; c != null; c = (c as ILayer<IToolbox>)?.Inner)
            if (c is TL match) return match;
        return null;
    }

    public static IToolbox Configure(
        this IToolbox? toolbox,
        IEnumerable<ITool>? tools = null, ILogger<Toolbox>? logger = null,
        bool parallel = true, int? maxConcurrency = null, TimeSpan? timeout = null)
            => new Toolbox(tools, logger, parallel, maxConcurrency, timeout);
}
