namespace AgentCore.Tool;

public static class ToolboxExtensions
{
    public static Toolbox AddTool(this Toolbox toolbox, IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(tools);
        return toolbox.With([.. toolbox.Tools, .. tools]);
    }

    public static Toolbox RemoveTool(this Toolbox toolbox, string name)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(name);
        return toolbox.With(toolbox.Tools.Where(t => !string.Equals(t.Info.Name, name, StringComparison.OrdinalIgnoreCase)));
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
