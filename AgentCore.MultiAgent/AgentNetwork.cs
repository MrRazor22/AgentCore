using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AgentCore.LLM.Chat;
using AgentCore.Tooling;

namespace AgentCore.MultiAgent;

public sealed record AgentDefinition(string Name, string Description);

public interface IAgentNetwork
{
    IReadOnlyList<AgentDefinition> GetAgents(string sender);
    IAsyncEnumerable<IContentEvent> SendStreamingAsync(string sender, string recipient, IEnumerable<IContent> task, CancellationToken ct = default);
}

public sealed class AgentNetwork(
    Func<string, string, string, (IAgent Agent, string Description, IEnumerable<string>? Collaborators)>? factory = null) : IAgentNetwork
{
    private sealed class AgentEntry(IAgent agent, string description, HashSet<string>? collaborators)
    {
        public IAgent Agent { get; } = agent;
        public string Description { get; } = description;
        public HashSet<string>? Collaborators { get; set; } = collaborators;
    }

    private readonly ConcurrentDictionary<string, AgentEntry> _agents = new(StringComparer.OrdinalIgnoreCase);

    public void Register(
        string name,
        IAgent agent,
        string description,
        IEnumerable<string>? collaborators = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(agent);

        _agents[name] = new AgentEntry(
            agent,
            description ?? string.Empty,
            collaborators != null ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) : null);
    }

    public bool Unregister(string name) => _agents.TryRemove(name, out _);

    public bool TryGet(string name, out IAgent? agent, out string? description)
    {
        if (_agents.TryGetValue(name, out var entry))
        {
            agent = entry.Agent;
            description = entry.Description;
            return true;
        }
        agent = null;
        description = null;
        return false;
    }

    public IReadOnlyList<AgentDefinition> GetAgents(string sender)
    {
        _agents.TryGetValue(sender, out var senderEntry);
        var allowedCollaborators = senderEntry?.Collaborators;

        return _agents
            .Where(kv => !string.Equals(kv.Key, sender, StringComparison.OrdinalIgnoreCase))
            .Where(kv => allowedCollaborators == null || allowedCollaborators.Contains(kv.Key))
            .Select(kv => new AgentDefinition(kv.Key, kv.Value.Description))
            .ToArray();
    }

    public async IAsyncEnumerable<IContentEvent> SendStreamingAsync(
        string sender,
        string recipient,
        IEnumerable<IContent> task,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ValidateRoute(sender, recipient, out var target);
        await foreach (var evt in target.InvokeStreamingAsync(task, ct: ct).ConfigureAwait(false))
            yield return evt;
    }

    public string CreateAgent(
        string creator,
        string name,
        string role,
        IEnumerable<string>? collaborators = null)
    {
        if (factory == null) return "Error: Dynamic agent creation is not enabled on this network.";
        if (string.IsNullOrWhiteSpace(name)) return "Error: Agent name cannot be empty.";
        if (_agents.ContainsKey(name)) return $"Error: Agent '{name}' already exists.";

        var (agent, description, factoryCollaborators) = factory(creator, name, role);

        // Explicit tool collaborators override or fall back to factory collaborators, defaulting to creator
        var specified = collaborators ?? factoryCollaborators;
        var childCollaborators = specified != null
            ? new HashSet<string>(specified, StringComparer.OrdinalIgnoreCase) { creator }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { creator };

        Register(name, agent, description, childCollaborators);

        // Automatically allow creator to reach the newly created collaborator
        if (_agents.TryGetValue(creator, out var creatorEntry) && creatorEntry.Collaborators != null)
        {
            creatorEntry.Collaborators.Add(name);
        }

        return $"Agent '{name}' created successfully and joined the network.";
    }

    public IEnumerable<ITool> GetToolsFor(string agentName)
    {
        yield return new SendAgentTool(this, agentName);
        if (factory != null)
            yield return new CreateAgentTool(this, agentName);
    }

    private void ValidateRoute(string sender, string recipient, out IAgent target)
    {
        if (!_agents.TryGetValue(recipient, out var entry))
            throw new KeyNotFoundException($"Agent '{recipient}' is not registered in the network.");

        if (_agents.TryGetValue(sender, out var senderEntry) && senderEntry.Collaborators != null)
        {
            if (!senderEntry.Collaborators.Contains(recipient))
                throw new InvalidOperationException($"Agent '{sender}' is not permitted to communicate with '{recipient}'.");
        }

        target = entry.Agent;
    }
}
