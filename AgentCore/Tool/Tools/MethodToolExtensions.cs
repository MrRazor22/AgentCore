using AgentCore.LLM.Chat;
using System.Reflection;

namespace AgentCore.Tool.Tools;

public static class MethodToolExtensions
{
    public static IToolbox AddTool<T>(this IToolbox toolbox, params IMetadata[] metadata)
        => toolbox.AddTool(typeof(T), null, metadata);

    public static IToolbox AddTool(this IToolbox toolbox, object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(instance);
        return instance is ITool tool
            ? toolbox.AddTool([tool])
            : toolbox.AddTool(instance.GetType(), instance, metadata);
    }

    private static IToolbox AddTool(this IToolbox toolbox, Type type, object? target, IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        var tools = type.GetMethods(flags)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null)
            .Select(m => (ITool)new MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return toolbox.AddTool(tools);
    }
}
