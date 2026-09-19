using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

public static class ToolingLayerExtensions
{
    public static ITooling UseApproval(this ITooling tooling, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(approver);
        return tooling.AddLayer(new ToolApprovalLayer(tooling, approver));
    }

    public static ITooling UseApproval(this ITooling tooling, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(prompt);
        return tooling.AddLayer(new ToolApprovalLayer(tooling, prompt));
    }

    public static ITooling RemoveApproval(this ITooling tooling)
        => tooling.RemoveLayer<ToolApprovalLayer>();

    public static ITooling UseToolDiscovery(this ITooling tooling, ToolDiscoveryTool tool)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(tool);
        return tooling.AddLayer(new ToolDiscoveryLayer(tool, tooling));
    }

    public static ITooling RemoveToolDiscovery(this ITooling tooling)
        => tooling.RemoveLayer<ToolDiscoveryLayer>();
}
