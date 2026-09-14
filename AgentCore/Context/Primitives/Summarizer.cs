using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context.Primitives;

public interface ICompactor
{
    Task<IReadOnlyList<Message>> CompactAsync(
        IReadOnlyList<Message> messages,
        int tokenLimit,
        CancellationToken ct = default);
}

public class Summarizer(
    ILLM llm,
    string prompt = "Please summarize our conversation so far, focusing on key details, facts, preferences, and decisions. Keep it concise.") : ICompactor
{
    private readonly ILLM _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    private readonly string _prompt = prompt;

    public async Task<IReadOnlyList<Message>> CompactAsync(
        IReadOnlyList<Message> messages,
        int tokenLimit,
        CancellationToken ct = default)
    {
        if (messages.Count == 0) return messages;

        var request = new List<Message>(messages.Count + 1);
        request.AddRange(messages);
        request.Add(new Message(Role.User, [new Text(_prompt)]));

        var summaryMsg = await _llm.GenerateAsync(request, ct: ct).ToMessageAsync(ct: ct).ConfigureAwait(false);
        var summaryText = summaryMsg.Contents.OfType<Text>().FirstOrDefault()?.Value?.Trim() ?? string.Empty;

        return BuildCompactedHistory(messages, summaryText);
    }

    private static IReadOnlyList<Message> BuildCompactedHistory(IReadOnlyList<Message> original, string summary)
    {
        var result = new List<Message>(3);
        var systemMessage = original.FirstOrDefault(m => m.Role == Role.System);
        var lastMessage = original.Count > (systemMessage != null ? 2 : 1) ? original[^1] : null;

        if (systemMessage != null) result.Add(systemMessage);
        result.Add(new Message(
            Role.User, 
            [new Text($"Context compacted due to overflow. Summary of previous interactions:\n{summary}")],
            [new Summary(original.Count)]));

        if (lastMessage != null && lastMessage.Role != Role.System)
        {
            result.Add(lastMessage);
        }
        return result;
    }
}
