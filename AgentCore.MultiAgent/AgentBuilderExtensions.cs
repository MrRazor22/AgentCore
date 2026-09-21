using System.Text.Json;
using AgentCore.MultiAgent.Tools;
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

        agent.Toolbox.AddTool(new SendAgentTool(team, name));
        var collabs = collaborators != null ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) : null;
        team.Add(new TeamMember(name, agent, collabs, description));
        return agent;
    }

    public static IAgentTeam AddFromJson(
        this IAgentTeam team,
        string json,
        Func<string, Agent> agentFactory)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(agentFactory);

        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.GetProperty("members");

        foreach (var el in array.EnumerateArray())
        {
            var name = el.GetProperty("name").GetString()!;
            var desc = el.TryGetProperty("description", out var d) ? d.GetString() : null;
            var collabs = el.TryGetProperty("collaborators", out var c)
                ? c.EnumerateArray().Select(x => x.GetString()!)
                : null;

            agentFactory(name).AddToTeam(team, name, collabs, desc);
        }

        return team;
    }
}

