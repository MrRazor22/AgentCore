using AgentCore.Tool;
using AgentCore.Tool.Tools;

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

        var tooling = (agent.Toolbox as Toolbox ?? new Toolbox()).AddTool(new SendAgentTool(team, name));
        var configuredAgent = agent.With(toolbox: tooling);
        var collabs = collaborators != null ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) : null;
        team.Add(new TeamMember(name, configuredAgent, collabs, description));
        return configuredAgent;
    }
}
