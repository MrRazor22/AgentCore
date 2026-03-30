using System.Linq.Expressions;
using System.Reflection;

namespace AgentCore.Tooling;

public static class ToolRegistryExtensions
{
    public static void RegisterAll<T>(this IToolRegistry registry)
    {
        RegisterMethods(
            BindingFlags.Static,
            typeof(T),
            CreateStaticDelegate,
            registry);
    }

    public static void RegisterAll<T>(this IToolRegistry registry, T instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        RegisterMethods(
            BindingFlags.Instance,
            typeof(T),
            m => CreateInstanceDelegate(instance, m),
            registry);
    }

    #region helpers
    private static void RegisterMethods(
        BindingFlags binding,
        Type type,
        Func<MethodInfo, Delegate?> delegateFactory,
        IToolRegistry registry)
    {
        var methods = type
            .GetMethods(BindingFlags.Public | binding)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null);

        foreach (var method in methods)
        {
            var del = delegateFactory(method);
            if (del != null)
                registry.Register(del);
        }
    }

    private static Delegate CreateStaticDelegate(MethodInfo method)
    {
        var delegateType = Expression.GetDelegateType(
            method.GetParameters()
                  .Select(p => p.ParameterType)
                  .Concat(new[] { method.ReturnType })
                  .ToArray());

        return Delegate.CreateDelegate(delegateType, method);
    }

    private static Delegate? CreateInstanceDelegate(
        object instance,
        MethodInfo method)
    {
        var delegateType = Expression.GetDelegateType(
            method.GetParameters()
                  .Select(p => p.ParameterType)
                  .Concat(new[] { method.ReturnType })
                  .ToArray());

        return Delegate.CreateDelegate(delegateType, instance, method, throwOnBindFailure: false);
    }
    #endregion
}