using AgentCore.LLM.Chat;
using AgentCore.Tooling;

namespace AgentCore.Layers.Tools;

public static class ToolingBuilderExtensions
{
    public static ToolingBuilder WithApproval(this ToolingBuilder builder, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(approver);
        return builder.AddLayer(new ToolApprovalLayer(approver));
    }

    public static ToolingBuilder WithApproval(this ToolingBuilder builder, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(prompt);
        return builder.AddLayer(new ToolApprovalLayer(prompt));
    }

    public static ToolingBuilder AddApprovalLayer(this ToolingBuilder builder, ToolApprovalLayer layer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(layer);
        return builder.AddLayer(layer);
    }

    public static ToolingBuilder WithDiscovery(
        this ToolingBuilder builder,
        IEnumerable<string>? coreTools = null,
        IToolSearcher? searcher = null,
        string discoveryToolName = "search_tools",
        string discoveryToolDescription = "Search available tools in the catalog and activate them for the session.")
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ToolDiscoveryLayer(coreTools, searcher, discoveryToolName, discoveryToolDescription));
    }
}
