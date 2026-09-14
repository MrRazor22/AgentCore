using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AgentCore.LLM.Chat;

namespace AgentCore.MultiAgent;

public sealed record NetworkMessage(string Sender, string Recipient, IEnumerable<IContent> Contents)
{
    public NetworkMessage(string sender, string recipient, params IContent[] contents)
        : this(sender, recipient, (IEnumerable<IContent>)contents) { }
}

public sealed record NetworkEvent(string Sender, string Recipient, IContentEvent Event);

public interface IAgentNetwork
{
    Task SendAsync(NetworkMessage message, CancellationToken ct = default);
    IAsyncEnumerable<NetworkEvent> ReceiveAsync(CancellationToken ct = default);
}

public sealed class AgentNetwork(IAgentRouter router) : IAgentNetwork
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<NetworkMessage>> _mailboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _signal = new(0);

    public Task SendAsync(NetworkMessage message, CancellationToken ct = default)
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
        _signal.Release();

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<NetworkEvent> ReceiveAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!TryGetNextPending(out var targetAgent, out var currentMsg))
            {
                try
                {
                    await _signal.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                continue;
            }

            if (!router.Agents.TryGetValue(targetAgent, out var entry))
                continue;

            var prompt = string.Equals(currentMsg.Sender, "User", StringComparison.OrdinalIgnoreCase)
                ? currentMsg.Contents
                : PrependSender(currentMsg.Sender, currentMsg.Contents);

            List<IContent> reply = [];

            await foreach (var evt in entry.Agent.InvokeStreamingAsync(prompt, ct: ct).ConfigureAwait(false))
            {
                if (evt is IContent c)
                    reply.Add(c);

                yield return new NetworkEvent(Sender: targetAgent, Recipient: currentMsg.Sender, Event: evt);
            }

            if (reply.Count > 0 &&
                !string.Equals(currentMsg.Sender, "User", StringComparison.OrdinalIgnoreCase) &&
                router.Agents.ContainsKey(currentMsg.Sender))
            {
                await SendAsync(new NetworkMessage(Sender: targetAgent, Recipient: currentMsg.Sender, Contents: reply), ct).ConfigureAwait(false);
            }
        }
    }

    private bool TryGetNextPending(out string targetAgent, out NetworkMessage message)
    {
        foreach (var (agentName, mailbox) in _mailboxes)
        {
            if (mailbox.TryDequeue(out var msg))
            {
                targetAgent = agentName;
                message = msg;
                return true;
            }
        }
        targetAgent = string.Empty;
        message = null!;
        return false;
    }

    private static IEnumerable<IContent> PrependSender(string sender, IEnumerable<IContent> contents)
    {
        yield return new Text($"[Message from {sender}]:\n");
        foreach (var item in contents)
            yield return item;
    }
}
