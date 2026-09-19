namespace AgentCore.Tool;

public static class ToolExtensions
{
    public static ITooling AddLayer(this ITooling tooling, ToolingLayer layer)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(tooling);
        return layer;
    }

    public static ITooling RemoveLayer<T>(this ITooling tooling) where T : class
    {
        if (tooling is T layer && layer is ToolingLayer tl)
            return tl.Inner.RemoveLayer<T>();

        if (tooling is ToolingLayer parent)
        {
            var newInner = parent.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, parent.Inner))
                parent.Attach(newInner);
        }
        return tooling;
    }
}
