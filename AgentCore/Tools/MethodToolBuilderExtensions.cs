using System;

namespace AgentCore.Tools;

public static class MethodToolBuilderExtensions
{
    public static AgentBuilder WithTools<T>(this AgentBuilder builder)
    {
        return builder.WithTools(typeof(T));
    }

    public static AgentBuilder WithTools(this AgentBuilder builder, object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        
        // If they passed a Type directly by accident, route to Type-based lookup
        if (instance is Type type)
        {
            return builder.WithTools(type);
        }

        foreach (var tool in MethodTool.FromType(instance.GetType(), instance))
        {
            builder.WithTools(tool);
        }

        return builder;
    }

    public static AgentBuilder WithTools(this AgentBuilder builder, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        foreach (var tool in MethodTool.FromType(type))
        {
            builder.WithTools(tool);
        }
        return builder;
    }
}
