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

    public static Agent WithApproval(this Agent agent, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.WithTooling(agent.Tooling.WithApproval(approver));
    }

    public static Agent WithApproval(this Agent agent, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.WithTooling(agent.Tooling.WithApproval(prompt));
    }

    public static Agent WithToolDiscovery(this Agent agent, ToolDiscoveryTool? tool = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var discovery = tool ?? new ToolDiscoveryTool();
        return agent
            .WithTools([.. agent.Tools, discovery])
            .WithTooling(new ToolDiscoveryLayer(discovery, agent.Tooling));
    }
}
