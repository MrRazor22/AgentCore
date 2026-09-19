using AgentCore.LLM.Chat;
using System.Reflection;

namespace AgentCore.Tool.Tools;

public static class MethodToolExtensions
{
    public static Tooling AddTool<T>(this Tooling tooling, params IMetadata[] metadata)
        => tooling.AddTool(typeof(T), null, metadata);

    public static Tooling AddTool(this Tooling tooling, object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(instance);
        return instance is ITool tool
            ? tooling.With([.. tooling.Tools, tool])
            : tooling.AddTool(instance.GetType(), instance, metadata);
    }

    private static Tooling AddTool(this Tooling tooling, Type type, object? target, IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        var tools = type.GetMethods(flags)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null)
            .Select(m => (ITool)new MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return tooling.With([.. tooling.Tools, .. tools]);
    }
}
