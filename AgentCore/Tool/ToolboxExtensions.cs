using Microsoft.Extensions.Logging;

namespace AgentCore.Tool;

public static class ToolboxExtensions
{
    public static async ValueTask<IReadOnlyList<ToolDefinition>> GetDefinitionsAsync(this IToolbox toolbox, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        var tools = await toolbox.GetToolsAsync(ct).ConfigureAwait(false);
        return tools.Select(t => t.Definition).ToArray();
    }

    public static IToolbox AddTool(this IToolbox toolbox, ITool tool)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(tool);
        toolbox.Find<Toolbox>()?.Add([tool]);
        return toolbox;
    }

    public static IToolbox AddTool(this IToolbox toolbox, IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(tools);
        toolbox.Find<Toolbox>()?.Add(tools);
        return toolbox;
    }

    public static TTarget? Find<TTarget>(this IToolbox? toolbox, Action<TTarget>? configure = null) where TTarget : class
    {
        for (var curr = toolbox; curr != null; curr = curr is ILayer<IToolbox> l ? l.Inner : null)
            if (curr is TTarget match) { configure?.Invoke(match); return match; }
        return null;
    }

    public static IToolbox Configure(
        this IToolbox? toolbox,
        IEnumerable<ITool>? tools = null, ILogger<Toolbox>? logger = null,
        bool parallel = true, int? maxConcurrency = null, TimeSpan? timeout = null)
            => new Toolbox(tools, logger, parallel, maxConcurrency, timeout);
}
