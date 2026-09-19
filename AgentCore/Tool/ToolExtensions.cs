using System.Reflection;
using AgentCore.LLM.Chat;
using AgentCore.Tool.Tools;

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

    public static Agent AddTools(this Agent agent, params ITool[] newTools)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(newTools);
        return agent.With(tools: [.. agent.Tools, .. newTools]);
    }

    public static Agent AddTools<T>(this Agent agent) => agent.AddTools(typeof(T));

    public static Agent AddTools(this Agent agent, object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance as Type ?? instance.GetType();
        var target = instance is Type ? null : instance;
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        var extracted = type.GetMethods(flags)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null)
            .Select(m => (ITool)new MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return agent.AddTools(extracted.ToArray());
    }

    public static Agent RemoveTool(this Agent agent, string name)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.With(tools: agent.Tools.Where(t => !string.Equals(t.Info.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    public static Agent RemoveTools(this Agent agent, Func<ITool, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(predicate);
        return agent.With(tools: agent.Tools.Where(t => !predicate(t)).ToArray());
    }
}
