using System.Collections.Concurrent;
using AgentCore.LLM.Chat;
using AgentCore.MultiAgent.Tools;
using AgentCore.Tooling;

namespace AgentCore.MultiAgent;

public sealed record NetworkMessage(string Sender, string Recipient, IEnumerable<IContent> Contents);

public interface IAgentNetwork
{
    void Send(NetworkMessage message); 
    string CreateAgent(string creator, string name, string role, IEnumerable<string>? collaborators = null);
}

public sealed class AgentNetwork(IAgentRouter router, Action<AgentBuilder>? configureDefaults = null) : IAgentNetwork
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<NetworkMessage>> _mailboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _processing = new(StringComparer.OrdinalIgnoreCase);
    private Action<AgentBuilder>? _configureDefaults = configureDefaults;

    public AgentNetwork(Action<AgentBuilder>? configureDefaults = null)
        : this(new AgentRouter(), configureDefaults) { }

    public AgentNetwork WithDefaults(Action<AgentBuilder> configureDefaults)
    {
        _configureDefaults = configureDefaults ?? throw new ArgumentNullException(nameof(configureDefaults));
        return this;
    }

    public void Send(NetworkMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Recipient);
        ArgumentNullException.ThrowIfNull(message.Contents);

        if (string.Equals(message.Sender, message.Recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An agent cannot send a message to itself.");

        if (!router.Agents.TryGetValue(message.Recipient, out _))
            throw new KeyNotFoundException($"Agent '{message.Recipient}' is not registered in the network.");

        if (router.Agents.TryGetValue(message.Sender, out var senderEntry) && senderEntry.Collaborators != null)
        {
            if (!senderEntry.Collaborators.Contains(message.Recipient))
                throw new InvalidOperationException($"Agent '{message.Sender}' is not permitted to communicate with '{message.Recipient}'.");
        }

        var mailbox = _mailboxes.GetOrAdd(message.Recipient, _ => new ConcurrentQueue<NetworkMessage>());
        mailbox.Enqueue(message);

        _ = ProcessMailboxAsync(message.Recipient, mailbox);
    }

    private async Task ProcessMailboxAsync(string agentName, ConcurrentQueue<NetworkMessage> mailbox)
    {
        if (_processing.AddOrUpdate(agentName, 1, (_, current) => current == 0 ? 1 : current) != 1)
            return;

        try
        {
            while (mailbox.TryDequeue(out var msg))
            {
                if (!router.Agents.TryGetValue(agentName, out var entry))
                    continue;

                var prompt = string.Equals(msg.Sender, "User", StringComparison.OrdinalIgnoreCase)
                    ? msg.Contents
                    : PrependSender(msg.Sender, msg.Contents);

                List<IContent> reply = [];
                try
                {
                    await foreach (var evt in entry.Agent.InvokeStreamingAsync(prompt).ConfigureAwait(false))
                    {
                        if (evt is IContent c)
                            reply.Add(c);
                    }
                }
                catch (Exception ex)
                {
                    reply.Add(new Text($"[Error during execution: {ex.Message}]"));
                }

                // If agent generated output and sender is a registered agent, route output back
                if (reply.Count > 0 &&
                    !string.Equals(msg.Sender, "User", StringComparison.OrdinalIgnoreCase) &&
                    router.Agents.ContainsKey(msg.Sender))
                {
                    Send(new NetworkMessage(Sender: agentName, Recipient: msg.Sender, Contents: reply));
                }
            }
        }
        finally
        {
            _processing[agentName] = 0;
            if (!mailbox.IsEmpty)
                _ = ProcessMailboxAsync(agentName, mailbox);
        }
    }

    private static IEnumerable<IContent> PrependSender(string sender, IEnumerable<IContent> contents)
    {
        yield return new Text($"[Message from {sender}]:\n");
        foreach (var item in contents)
            yield return item;
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
        if (router.Agents.ContainsKey(name))
            return $"Error: Agent '{name}' already exists.";

        var builder = new AgentBuilder();
        _configureDefaults(builder);
        builder.WithInstructions(role);
        builder.UseToolbox(t => t.WithTools(new SendAgentTool(this, router, name), new CreateAgentTool(this, name)));

        var childCollaborators = collaborators != null
            ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) { creator }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { creator };

        router.Register(name, builder.Build(), childCollaborators, description: role);

        if (router.Agents.TryGetValue(creator, out var creatorEntry) && creatorEntry.Collaborators != null)
        {
            creatorEntry.Collaborators.Add(name);
        }

        return $"Agent '{name}' created successfully and joined the network.";
    }
}
