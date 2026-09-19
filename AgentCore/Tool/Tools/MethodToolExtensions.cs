using AgentCore.LLM.Chat;
using System.Reflection;

namespace AgentCore.Tool.Tools;

public static class MethodToolExtensions
{
    public static Agent AddTool<T>(this Agent agent, params IMetadata[] metadata)
        => agent.AddTool(typeof(T), null, metadata);

    public static Agent AddTool(this Agent agent, object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance is ITool tool
            ? agent.With(tools: [.. agent.Tools, tool])
            : agent.AddTool(instance.GetType(), instance, metadata);
    }

    private static Agent AddTool(this Agent agent, Type type, object? target, IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        var tools = type.GetMethods(flags)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null)
            .Select(m => (ITool)new MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return agent.With(tools: [.. agent.Tools, .. tools]);
    }
}
