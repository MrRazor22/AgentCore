using System.Reflection;
using Microsoft.Extensions.Logging;

namespace AgentCore.Tool;

public sealed class ToolingBuilder
{
    internal List<ITool> Tools { get; } = [];
    internal Func<IReadOnlyList<ITool>, ILoggerFactory, ITooling>? Factory { get; private set; }
    internal List<ToolingLayer> Layers { get; } = [];

    public ToolingBuilder WithTools(params ITool[] tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            Tools.Add(tool);
        }
        return this;
    }

    public ToolingBuilder WithTools<T>() => WithTools(typeof(T));

    public ToolingBuilder WithTools(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance as Type ?? instance.GetType();
        var target = instance is Type ? null : instance;
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        foreach (var m in type.GetMethods(flags).Where(m => m.GetCustomAttribute<ToolAttribute>() != null))
            WithTools(new MethodTool(m, m.IsStatic ? null : target));

        return this;
    }

    public ToolingBuilder WithTools(Type type) => WithTools((object)type);

    public ToolingBuilder Use(Func<IReadOnlyList<ITool>, ILoggerFactory, ITooling> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
        return this;
    }

    public ToolingBuilder Use(Func<ILoggerFactory, ITooling> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = (_, lf) => factory(lf);
        return this;
    }

    public ToolingBuilder WithExecutionOptions(
        bool parallel = true,
        int? maxConcurrency = null,
        TimeSpan? timeout = null)
    {
        return Use((tools, lf) => new Tooling(
            tools: tools,
            logger: lf.CreateLogger<Tooling>(),
            parallel: parallel,
            maxConcurrency: maxConcurrency,
            timeout: timeout
        ));
    }

    public ToolingBuilder AddLayer(ToolingLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Layers.Add(layer);
        return this;
    }

    internal ITooling Build(ILoggerFactory lf)
    {
        var frozenTools = Tools.ToArray();
        ITooling tooling = Factory != null
            ? Factory(frozenTools, lf)
            : new Tooling(frozenTools, lf.CreateLogger<Tooling>());

        foreach (var layer in Layers)
        {
            layer.Attach(tooling);
            tooling = layer;
        }

        return tooling;
    }
}
