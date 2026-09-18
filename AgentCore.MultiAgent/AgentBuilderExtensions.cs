using AgentCore;
using AgentCore.MultiAgent.Tools;

namespace AgentCore.MultiAgent;

public static class AgentTeamExtensions
{
    public static Agent AddToTeam(
        this Agent agent,
        IAgentTeam team,
        string name,
        IEnumerable<string>? collaborators = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(name);

        var configuredAgent = agent.WithTools([.. agent.Tools, new SendAgentTool(team, name)]);
        var collabs = collaborators != null ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) : null;
        team.Add(new TeamMember(name, configuredAgent, collabs, description));
        return configuredAgent;
    }
}
