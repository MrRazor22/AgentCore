using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

public static class ToolingLayerExtensions
{
    public static IToolbox UseApproval(this IToolbox tooling, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(approver);
        return tooling.AddLayer(new ToolApprovalLayer(tooling, approver));
    }

    public static IToolbox UseApproval(this IToolbox tooling, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(prompt);
        return tooling.AddLayer(new ToolApprovalLayer(tooling, prompt));
    }

    public static IToolbox RemoveApproval(this IToolbox tooling)
        => tooling.RemoveLayer<ToolApprovalLayer>();

    public static IToolbox UseToolDiscovery(this IToolbox tooling, ToolDiscoveryTool? tool = null)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        return tooling.AddLayer(new ToolDiscoveryLayer(tool ?? new ToolDiscoveryTool(), tooling));
    }

    public static IToolbox RemoveToolDiscovery(this IToolbox tooling)
        => tooling.RemoveLayer<ToolDiscoveryLayer>();
}
