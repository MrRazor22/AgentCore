using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

public static class ToolBuilderExtensions
{

    public static ToolBuilder WithApproval(this ToolBuilder builder, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(approver);
        return builder.AddLayer(new ToolApprovalLayer(approver));
    }

    public static ToolBuilder WithApproval(this ToolBuilder builder, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(prompt);
        return builder.AddLayer(new ToolApprovalLayer(prompt));
    }

    public static ToolBuilder AddApprovalLayer(this ToolBuilder builder, ToolApprovalLayer layer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(layer);
        return builder.AddLayer(layer);
    }

    public static ToolBuilder WithToolDiscovery(this ToolBuilder builder, ToolDiscoveryTool? tool = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var discovery = tool ?? new ToolDiscoveryTool();
        builder.WithTools(discovery);
        return builder.AddLayer(new ToolDiscoveryLayer(discovery));
    }
}
