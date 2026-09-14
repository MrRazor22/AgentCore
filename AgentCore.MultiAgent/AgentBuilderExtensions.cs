using AgentCore;
using AgentCore.MultiAgent.Tools;

namespace AgentCore.MultiAgent;

public static class AgentBuilderExtensions
{
    public static Agent AddToNetwork(
        this AgentBuilder builder,
        IAgentNetwork network,
        IAgentRouter router,
        string name,
        IEnumerable<string>? collaborators = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(name);

        builder.UseToolbox(t => t.WithTools(new SendAgentTool(network, router, name)));
        var agent = builder.Build();
        router.Register(name, agent, collaborators, description);
        return agent;
    }
}
