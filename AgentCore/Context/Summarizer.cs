using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public interface ICompactor
{
    Task<IReadOnlyList<Message>> CompactAsync(
        IReadOnlyList<Message> messages,
        int tokenLimit,
        CancellationToken ct = default);
}
public class Summarizer : ICompactor
{
    private readonly ILLM _llm;
    private readonly ILogger<Summarizer>? _logger;

    public Summarizer(ILLM llm, ILogger<Summarizer>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    public async Task<IReadOnlyList<Message>> CompactAsync(
        IReadOnlyList<Message> messages,
        int tokenLimit,
        CancellationToken ct = default)
    {
        var history = messages.ToList();
        history.Add(new Message(Role.User, [new Text("Please summarize our conversation so far, focusing on key details, facts, preferences, and decisions. Keep it concise.")]));

        while (true)
        {
            try
            {
                var eventStream = _llm.StreamAsync(history, responseSchema: null, tools: null, ct: ct);
                var message = await new StreamingMessage(Role.Assistant).ToMessageAsync(eventStream, ct).ConfigureAwait(false);
                var summary = message.Contents.OfType<Text>().FirstOrDefault()?.Value ?? string.Empty;
                return BuildCompactedHistory(messages, summary.Trim());
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                int oldestIndex = history.Count > 0 && history[0].Role == Role.System ? 1 : 0;
                if (history.Count > oldestIndex + 1)
                {
                    var removed = history[oldestIndex];
                    history.RemoveAt(oldestIndex);
                    _logger?.LogWarning(ex, "Compaction prompt exceeded context window. Removed oldest item ({Role}) and retrying.", removed.Role);
                }
                else
                {
                    throw;
                }
            }
        }
    }

    private static IReadOnlyList<Message> BuildCompactedHistory(IReadOnlyList<Message> original, string summary)
    {
        var result = new List<Message>();
        var systemMessage = original.FirstOrDefault(m => m.Role == Role.System);
        var lastMessage = original.Count > (systemMessage != null ? 2 : 1) ? original[^1] : null;

        if (systemMessage != null) result.Add(systemMessage);
        result.Add(new Message(Role.User, [new CompactedSummary($"Context compacted due to overflow. Summary of previous interactions:\n{summary}")]));
        if (lastMessage != null && lastMessage.Role != Role.System)
        {
            result.Add(lastMessage);
        }
        return result;
    }
}
