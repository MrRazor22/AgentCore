using System.Reflection;
using AgentCore.LLM.Chat;
using AgentCore.Tool.Tools;
using Microsoft.Extensions.Logging;

namespace AgentCore.Tool;

public sealed class ToolBuilder
{
    internal List<ITool> Tools { get; } = [];
    internal Func<IReadOnlyList<ITool>, ILoggerFactory, ITooling>? Factory { get; private set; }
    internal List<ToolingLayer> Layers { get; } = [];

    public ToolBuilder WithTools(params ITool[] tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            Tools.Add(tool);
        }
        return this;
    }

    public ToolBuilder WithTools<T>() => WithTools(typeof(T));

    public ToolBuilder WithTools(object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance as Type ?? instance.GetType();
        var target = instance is Type ? null : instance;
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        foreach (var m in type.GetMethods(flags).Where(m => m.GetCustomAttribute<ToolAttribute>() != null))
            WithTools(new MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return this;
    }

    public ToolBuilder WithTools(Type type) => WithTools((object)type);

    public ToolBuilder Use(Func<IReadOnlyList<ITool>, ILoggerFactory, ITooling> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
        return this;
    }

    public ToolBuilder Use(Func<ILoggerFactory, ITooling> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = (_, lf) => factory(lf);
        return this;
    }

    public ToolBuilder WithExecutionOptions(
        bool parallel = true,
        int? maxConcurrency = null,
        TimeSpan? timeout = null)
    {
        return Use((tools, lf) => new Tooling(
            logger: lf.CreateLogger<Tooling>(),
            parallel: parallel,
            maxConcurrency: maxConcurrency,
            timeout: timeout
        ));
    }

    public ToolBuilder Use(ToolingDelegate middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        return AddLayer(new ToolingLayer(middleware));
    }

    public ToolBuilder AddLayer(ToolingLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Layers.Add(layer);
        return this;
    }

    internal (ITooling Toolbox, IReadOnlyList<ITool> Tools) Build(ILoggerFactory lf)
    {
        var frozenTools = Tools.ToArray();
        ITooling tooling = Factory != null
            ? Factory(frozenTools, lf)
            : new Tooling(lf.CreateLogger<Tooling>());

        foreach (var layer in Layers)
        {
            layer.Attach(tooling);
            tooling = layer;
        }

        return (tooling, frozenTools);
    }
}
