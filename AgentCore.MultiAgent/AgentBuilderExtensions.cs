using AgentCore;
using AgentCore.MultiAgent.Tools;

namespace AgentCore.MultiAgent;

public static class AgentBuilderExtensions
{
    public static Agent AddToTeam(
        this AgentBuilder builder,
        IAgentTeam team,
        string name,
        IEnumerable<string>? collaborators = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(name);

        builder.UseTool(t => t.WithTools(new SendAgentTool(team, name)));
        var agent = builder.Build();
        var collabs = collaborators != null ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) : null;
        team.Add(new TeamMember(name, agent, collabs, description));
        return agent;
    }
}
