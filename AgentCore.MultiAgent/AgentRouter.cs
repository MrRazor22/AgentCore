using System.Collections.Concurrent;
using AgentCore.LLM.Chat;

namespace AgentCore.MultiAgent;

public sealed record AgentEntry(
    IAgent Agent,
    HashSet<string>? Collaborators = null,
    string? Description = null)
{
    public string Description { get; } = Description
        ?? (Agent as Agent)?.Instructions?.OfType<Text>().FirstOrDefault()?.Value
        ?? string.Empty;
}

public interface IAgentRouter
{
    void Register(string name, IAgent agent, IEnumerable<string>? collaborators = null, string? description = null);
    bool Unregister(string name);
    IReadOnlyDictionary<string, AgentEntry> Agents { get; }
}

public sealed class AgentRouter : IAgentRouter
{
    private readonly ConcurrentDictionary<string, AgentEntry> _agents = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, AgentEntry> Agents => _agents;

    public void Register(
        string name,
        IAgent agent,
        IEnumerable<string>? collaborators = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(agent);

        _agents[name] = new AgentEntry(
            agent,
            collaborators != null ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) : null,
            description);
    }

    public bool Unregister(string name) => _agents.TryRemove(name, out _);
}
