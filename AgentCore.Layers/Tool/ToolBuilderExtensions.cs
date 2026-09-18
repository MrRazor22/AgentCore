using AgentCore;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

public static class ToolingExtensions
{
    public static ITooling WithApproval(this ITooling tooling, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(approver);
        return new ToolApprovalLayer(tooling, approver);
    }

    public static ITooling WithApproval(this ITooling tooling, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolApprovalLayer(tooling, prompt);
    }

    public static Agent UseApproval(this Agent agent, ToolApprover approver)
        => agent.AddLayer(new ToolApprovalLayer(agent.Tooling, approver));

    public static Agent UseApproval(this Agent agent, Func<ToolCall, CancellationToken, Task<bool>> prompt)
        => agent.AddLayer(new ToolApprovalLayer(agent.Tooling, prompt));

    public static Agent RemoveApproval(this Agent agent)
        => agent.RemoveLayer<ToolApprovalLayer>();

    public static Agent UseToolDiscovery(this Agent agent, ToolDiscoveryTool? tool = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var discovery = tool ?? new ToolDiscoveryTool();
        return agent
            .AddTools(discovery)
            .AddLayer(new ToolDiscoveryLayer(discovery, agent.Tooling));
    }

    public static Agent WithApproval(this Agent agent, ToolApprover approver)
        => agent.UseApproval(approver);

    public static Agent WithApproval(this Agent agent, Func<ToolCall, CancellationToken, Task<bool>> prompt)
        => agent.UseApproval(prompt);

    public static Agent WithoutApproval(this Agent agent)
        => agent.RemoveApproval();

    public static Agent WithToolDiscovery(this Agent agent, ToolDiscoveryTool? tool = null)
        => agent.UseToolDiscovery(tool);
}
