using System.Reflection;

namespace AgentCore.Tools;

public static class MethodToolBuilderExtensions
{
    public static AgentBuilder WithTools<T>(this AgentBuilder builder) => builder.WithTools(typeof(T));

    public static AgentBuilder WithTools(this AgentBuilder builder, object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance is Type type) return builder.WithTools(type);

        var methods = instance.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null);

        foreach (var method in methods)
        {
            builder.WithTools(new MethodTool(method, method.IsStatic ? null : instance));
        }

        return builder;
    }

    public static AgentBuilder WithTools(this AgentBuilder builder, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null);

        foreach (var method in methods)
        {
            builder.WithTools(new MethodTool(method));
        }

        return builder;
    }
}
