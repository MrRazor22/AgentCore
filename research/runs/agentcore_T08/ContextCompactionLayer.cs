using System.Runtime.CompilerServices;
using AgentCore;
using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.LLM.Chat;

namespace AgentCoreT08;

public sealed class ContextCompactionLayer(
    int tokenThreshold,
    int preserveLastTurns = 2,
    Func<IReadOnlyList<Message>, Task<string>>? summarizer = null,
    IContext? inner = null) : IContext
{
    public IContext Inner { get; } = inner!;
    private readonly int _tokenThreshold = tokenThreshold;
    private readonly int _preserveLastTurns = Math.Max(1, preserveLastTurns);
    private readonly Func<IReadOnlyList<Message>, Task<string>> _summarizer =
        summarizer ?? (msgs => Task.FromResult($"Summary of {msgs.Count} prior interactions. Key facts preserved."));

    public int CompactionCount { get; private set; }

    public static int EstimateTokens(Message message)
    {
        var chars = 0;
        foreach (var c in message.Contents)
        {
            chars += c is Text t ? t.Value.Length : (c.ToString()?.Length ?? 0);
        }
        return Math.Max(1, chars / 4);
    }

    public static int EstimateTokens(IEnumerable<Message> messages)
        => messages.Sum(EstimateTokens);

    public async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        var messages = Inner != null ? await Inner.ReadAsync(ct).ConfigureAwait(false) : [];
        if (messages.Count == 0) return messages;

        var totalTokens = EstimateTokens(messages);
        if (totalTokens <= _tokenThreshold) return messages;

        CompactionCount++;

        var system = messages.FirstOrDefault(m => m.Role == Role.System);
        var nonSystem = messages.Where(m => m.Role != Role.System).ToList();

        if (nonSystem.Count <= _preserveLastTurns) return messages;

        var toSummarize = nonSystem.Take(nonSystem.Count - _preserveLastTurns).ToList();
        var preserved = nonSystem.Skip(nonSystem.Count - _preserveLastTurns).ToList();

        var summaryText = await _summarizer(toSummarize).ConfigureAwait(false);

        var compacted = new List<Message>();
        if (system != null)
        {
            compacted.Add(system);
        }

        compacted.Add(new Message(
            Role.User,
            [new Text($"[Prior conversation summary]: {summaryText}")],
            metadata: [new Summary()]));

        compacted.AddRange(preserved);

        return compacted;
    }

    public IAsyncEnumerable<IContentEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        CancellationToken ct = default)
        => Inner != null ? Inner.WriteAsync(events, ct) : EmptyAsync();

    private static async IAsyncEnumerable<IContentEvent> EmptyAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}
