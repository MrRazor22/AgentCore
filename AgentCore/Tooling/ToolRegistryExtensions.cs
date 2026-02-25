using System.Linq.Expressions;
using System.Reflection;

namespace AgentCore.Tooling;

public static class ToolRegistryExtensions
{
    public static void RegisterAll<T>(this IToolRegistry registry)
    {
        RegisterMethods(
            typeof(T),
            CreateStaticDelegate,
            registry);
    }

    public static void RegisterAll<T>(this IToolRegistry registry, T instance)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        RegisterMethods(
            typeof(T),
            m => CreateInstanceDelegate(instance, m),
            registry);
    }

    #region helpers
    private static void RegisterMethods(
        Type type,
        Func<MethodInfo, Delegate?> delegateFactory,
        IToolRegistry registry)
    {
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
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
        return Delegate.CreateDelegate(
            Expression.GetDelegateType(
                method.GetParameters()
                      .Select(p => p.ParameterType)
                      .Concat(new[] { method.ReturnType })
                      .ToArray()),
            method);
    }

    private static Delegate? CreateInstanceDelegate(
        object instance,
        MethodInfo method)
    {
        return Delegate.CreateDelegate(
            Expression.GetDelegateType(
                method.GetParameters()
                      .Select(p => p.ParameterType)
                      .Concat(new[] { method.ReturnType })
                      .ToArray()),
            instance,
            method,
            throwOnBindFailure: false);
    }
    #endregion


    public static IToolRegistry AutoDiscoverFrom(
        this IToolRegistry registry,
        Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            registry.RegisterAll(type);
        }
        return registry;
    }
}


