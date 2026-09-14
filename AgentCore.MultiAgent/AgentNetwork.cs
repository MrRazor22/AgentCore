using System.Collections.Concurrent;
using AgentCore.LLM.Chat;
using AgentCore.MultiAgent.Tools;
using AgentCore.Tooling;

namespace AgentCore.MultiAgent;

public interface IAgentNetwork
{
    void Send(string sender, string recipient, IEnumerable<IContent> task);
    string CreateAgent(string creator, string name, string role, IEnumerable<string>? collaborators = null);
}

public sealed class AgentNetwork : IAgentNetwork
{
    private readonly IAgentRouter _router;
    private readonly ConcurrentQueue<(string Sender, string Recipient, IEnumerable<IContent> Task)> _queue = new();
    private Action<AgentBuilder>? _configureDefaults;

    public AgentNetwork(Action<AgentBuilder>? configureDefaults = null)
        : this(new AgentRouter(), configureDefaults) { }

    public AgentNetwork(IAgentRouter router, Action<AgentBuilder>? configureDefaults = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _configureDefaults = configureDefaults;
    }

    public AgentNetwork WithDefaults(Action<AgentBuilder> configureDefaults)
    {
        _configureDefaults = configureDefaults ?? throw new ArgumentNullException(nameof(configureDefaults));
        return this;
    }

    public void Send(string sender, string recipient, IEnumerable<IContent> task)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(task);

        if (string.Equals(sender, recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An agent cannot send a message to itself.");

        if (!_router.Agents.TryGetValue(recipient, out _))
            throw new KeyNotFoundException($"Agent '{recipient}' is not registered in the network.");

        if (_router.Agents.TryGetValue(sender, out var senderEntry) && senderEntry.Collaborators != null)
        {
            if (!senderEntry.Collaborators.Contains(recipient))
                throw new InvalidOperationException($"Agent '{sender}' is not permitted to communicate with '{recipient}'.");
        }

        _queue.Enqueue((sender, recipient, task));
    }

    public string CreateAgent(
        string creator,
        string name,
        string role,
        IEnumerable<string>? collaborators = null)
    {
        if (_configureDefaults == null)
            return "Error: Dynamic agent creation is not enabled on this network. Configure defaults first using WithDefaults(...).";
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Agent name cannot be empty.";
        if (_router.Agents.ContainsKey(name))
            return $"Error: Agent '{name}' already exists.";

        var builder = new AgentBuilder();
        _configureDefaults(builder);
        builder.WithInstructions(role);
        builder.UseToolbox(t => t.WithTools(GetTools(name)));

        var agent = builder.Build();

        var childCollaborators = collaborators != null
            ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) { creator }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { creator };

        _router.Register(name, agent, childCollaborators, description: role);

        // Automatically allow creator to reach the newly created collaborator
        if (_router.Agents.TryGetValue(creator, out var creatorEntry) && creatorEntry.Collaborators != null)
        {
            creatorEntry.Collaborators.Add(name);
        }

        return $"Agent '{name}' created successfully and joined the network.";
    }

    private IEnumerable<ITool> GetTools(string agentName)
    {
        yield return new SendAgentTool(this, _router, agentName);
        if (_configureDefaults != null)
            yield return new CreateAgentTool(this, agentName);
    }
}
