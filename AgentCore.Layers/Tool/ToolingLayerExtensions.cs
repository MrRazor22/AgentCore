using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

public static class ToolingLayerExtensions
{
    public static IToolbox UseApproval(this IToolbox tooling, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(approver);
        return tooling.Remove<ToolApprovalLayer>().Add(new ToolApprovalLayer(approver));
    }

    public static IToolbox UseApproval(this IToolbox tooling, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(prompt);
        return tooling.Remove<ToolApprovalLayer>().Add(new ToolApprovalLayer(prompt));
    }

    public static IToolbox RemoveApproval(this IToolbox tooling)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        return tooling.Remove<ToolApprovalLayer>();
    }

    public static IToolbox UseToolDiscovery(this IToolbox tooling, ToolDiscoveryTool? tool = null)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        return tooling.Remove<ToolDiscoveryLayer>().Add(new ToolDiscoveryLayer(tool ?? new ToolDiscoveryTool()));
    }

    public static IToolbox RemoveToolDiscovery(this IToolbox tooling)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        return tooling.Remove<ToolDiscoveryLayer>();
    }
}
