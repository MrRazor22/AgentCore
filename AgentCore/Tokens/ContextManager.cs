using AgentCore.Chat;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;

namespace AgentCore.Tokens;

public interface IContextManager
{
    IList<Message> Reduce(IList<Message> messages, LLMOptions options);
}

public sealed class ContextManager(
    ITokenCounter _counter,
    ILogger<ContextManager> _logger,
    int keepLastMessages = 4
) : IContextManager
{
    public IList<Message> Reduce(IList<Message> messages, LLMOptions options)
    {
        if (messages == null) throw new ArgumentNullException(nameof(messages));

        int contextLength = options.ContextLength
            ?? throw new InvalidOperationException(
                "ContextLength is required for context management. Set it in LLMOptions or via provider registration.");

        int reserveForOutput = options.MaxOutputTokens ?? 4096;
        int available = contextLength - reserveForOutput;

        if (available <= 0)
            throw new InvalidOperationException(
                $"No available context budget: ContextLength={contextLength}, MaxOutputTokens={reserveForOutput}.");

        int current = _counter.Count(messages.ToJson());

        if (current <= available)
            return messages;

        // --- Tail-trim: keep system messages + last N user/assistant, drop oldest ---

        var source = messages.Clone();
        var system = source.Where(m => m.Role == Role.System).ToList();

        var ua = source
            .Where(m => (m.Role == Role.User || m.Role == Role.Assistant) && m.Content is Text)
            .ToList();

        Message? lastToolMsg = source.LastOrDefault(m => m.Role == Role.Tool);

        var keepUA = ua.Skip(Math.Max(0, ua.Count - keepLastMessages)).ToList();

        Message? lastUser = null, lastAssistant = null;
        for (int i = ua.Count - 1; i >= 0; i--)
        {
            if (lastUser == null && ua[i].Role == Role.User) lastUser = ua[i];
            else if (lastUser != null && ua[i].Role == Role.Assistant) { lastAssistant = ua[i]; break; }
        }

        if (lastUser != null && !keepUA.Contains(lastUser)) keepUA.Add(lastUser);
        if (lastAssistant != null && !keepUA.Contains(lastAssistant)) keepUA.Add(lastAssistant);

        keepUA = keepUA.Distinct().OrderBy(m => source.IndexOf(m)).ToList();

        IList<Message> Build(IReadOnlyList<Message> uaSlice)
        {
            var result = new List<Message>();
            foreach (var s in system)
                result.Add(new Message(s.Role, s.Content));
            if (lastToolMsg != null)
                result.Add(new Message(lastToolMsg.Role, lastToolMsg.Content));
            foreach (var m in uaSlice)
                result.Add(new Message(m.Role, m.Content));
            return result;
        }

        var rebuilt = Build(keepUA);
        int tokens = _counter.Count(rebuilt.ToJson());

        while (tokens > available && keepUA.Count > 0)
        {
            keepUA.RemoveAt(0);
            rebuilt = Build(keepUA);
            tokens = _counter.Count(rebuilt.ToJson());
        }

        _logger.LogDebug("Context reduced: {Before}→{After} tokens (limit {Limit})",
            current, tokens, available);

        return rebuilt;
    }
}
