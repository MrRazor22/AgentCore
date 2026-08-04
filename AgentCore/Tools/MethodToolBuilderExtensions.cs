using System;

namespace AgentCore.Tools;

public static class MethodToolBuilderExtensions
{
    public static Agent.Builder WithTools<T>(this Agent.Builder builder)
    {
        return builder.WithTools(typeof(T));
    }

    public static Agent.Builder WithTools(this Agent.Builder builder, object instance)
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

    public static Agent.Builder WithTools(this Agent.Builder builder, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        foreach (var tool in MethodTool.FromType(type))
        {
            builder.WithTools(tool);
        }
        return builder;
    }
}
