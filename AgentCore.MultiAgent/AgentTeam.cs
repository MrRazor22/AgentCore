using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AgentCore.LLM.Chat;

namespace AgentCore.MultiAgent;

public sealed record TeamMessage(
    string Sender,
    string Recipient,
    IEnumerable<IContent> Contents,
    TimeSpan? NotifyInterval = null);

public sealed record TeamEvent(string Sender, string Recipient, IContentEvent Event);

public interface IAgentTeam
{
    IReadOnlyDictionary<string, ITeamMember> Members { get; }
    void Add(ITeamMember member);
    bool Remove(string name);
    Task SendAsync(TeamMessage message, CancellationToken ct = default);
    IAsyncEnumerable<TeamEvent> ReceiveAsync(CancellationToken ct = default);
}

public sealed class AgentTeam : IAgentTeam
{
    private readonly ConcurrentDictionary<string, ITeamMember> _members = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<TeamMessage> _channel = Channel.CreateUnbounded<TeamMessage>(new UnboundedChannelOptions { SingleReader = true });

    public IReadOnlyDictionary<string, ITeamMember> Members => _members;

    public void Add(ITeamMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        _members[member.Name] = member;
    }

    public bool Remove(string name) => _members.TryRemove(name, out _);

    public Task SendAsync(TeamMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Recipient);
        ArgumentNullException.ThrowIfNull(message.Contents);

        if (string.Equals(message.Sender, message.Recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An agent cannot send a message to itself.");

        if (!_members.TryGetValue(message.Recipient, out _))
            throw new KeyNotFoundException($"Agent '{message.Recipient}' is not a member of the team.");

        if (_members.TryGetValue(message.Sender, out var senderMember) && senderMember.Collaborators != null)
        {
            if (!senderMember.Collaborators.Contains(message.Recipient))
                throw new InvalidOperationException($"Agent '{message.Sender}' is not permitted to communicate with '{message.Recipient}'.");
        }

        _channel.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TeamEvent> ReceiveAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var currentMsg in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (!_members.TryGetValue(currentMsg.Recipient, out var member))
                continue;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task? progressTask = null;

            if (currentMsg.NotifyInterval is { TotalMilliseconds: > 0 } interval && _members.ContainsKey(currentMsg.Sender))
            {
                progressTask = RunProgressTimerAsync(member, currentMsg.Sender, interval, cts.Token);
            }

            List<IContent> reply = [];
            try
            {
                await foreach (var evt in member.ExecuteAsync(currentMsg.Contents, ct).ConfigureAwait(false))
                {
                    if (evt is IContent c)
                        reply.Add(c);

                    yield return new TeamEvent(Sender: currentMsg.Recipient, Recipient: currentMsg.Sender, Event: evt);
                }
            }
            finally
            {
                cts.Cancel();
                if (progressTask != null)
                {
                    try { await progressTask.ConfigureAwait(false); } catch { }
                }
            }

            // Deliver reply back to sender if sender is a registered team member
            if (_members.ContainsKey(currentMsg.Sender) && reply.Count > 0)
            {
                await SendAsync(new TeamMessage(Sender: currentMsg.Recipient, Recipient: currentMsg.Sender, Contents: reply), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RunProgressTimerAsync(
        ITeamMember member,
        string callerAgent,
        TimeSpan interval,
        CancellationToken token)
    {
        using var timer = new PeriodicTimer(interval);
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            var statusMsg = new TeamMessage(
                Sender: member.Name,
                Recipient: callerAgent,
                Contents: [new Text($"[Progress: Agent '{member.Name}' is {member.Status}.]")]);

            try
            {
                await SendAsync(statusMsg, token).ConfigureAwait(false);
            }
            catch { }
        }
    }
}
