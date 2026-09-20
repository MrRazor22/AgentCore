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

    public static IToolbox AddLayer(this IToolbox toolbox, ToolboxLayer layer)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(toolbox);
        return layer;
    }

    public static IToolbox RemoveLayer<T>(this IToolbox toolbox) where T : class
    {
        if (toolbox is T layer && layer is ToolboxLayer tl)
            return tl.Inner.RemoveLayer<T>();

        if (toolbox is ToolboxLayer parent)
        {
            var newInner = parent.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, parent.Inner))
                parent.Attach(newInner);
        }
        return toolbox;
    }
}
