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
    string prompt = "Please summarize our conversation so far, focusing on key details, facts, preferences, and decisions. Keep it concise.",
    INormalizer? normalizer = null) : ICompactor
{
    private readonly ILLM _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    private readonly string _prompt = prompt;
    private readonly INormalizer _normalizer = normalizer ?? new ChatNormalizer();

    public async Task<IReadOnlyList<Message>> CompactAsync(
        IReadOnlyList<Message> messages,
        int tokenLimit,
        CancellationToken ct = default)
    {
        if (messages.Count == 0) return messages;

        var request = _normalizer.Normalize([.. messages, new Message(Role.User, [new Text(_prompt)])]);

        var summaryMsgs = await _llm.GenerateAsync(request, ct: ct).ToMessagesAsync(ct: ct).ConfigureAwait(false);
        var summaryText = summaryMsgs.FirstOrDefault()?.Contents.OfType<Text>().FirstOrDefault()?.Value?.Trim() ?? string.Empty;

        return BuildCompactedHistory(messages, summaryText);
    }

    private static IReadOnlyList<Message> BuildCompactedHistory(IReadOnlyList<Message> original, string summary)
    {
        var result = new List<Message>();
        var systemMessage = original.FirstOrDefault(m => m.Role == Role.System);
        if (systemMessage != null) result.Add(systemMessage);

        result.Add(new Message(
            Role.User, 
            [new Text($"Context compacted due to overflow. Summary of previous interactions:\n{summary}")],
            [new Summary(original.Count)]));

        foreach (var msg in GetTrailingTurn(original))
            if (msg.Role != Role.System)
                result.Add(msg);

        return result;
    }

    private static IReadOnlyList<Message> GetTrailingTurn(IReadOnlyList<Message> original)
    {
        if (original.Count == 0) return [];
        int i = original.Count - 1;
        if (original[i].Role == Role.Tool)
        {
            while (i > 0 && original[i].Role == Role.Tool) i--;
            return original.Skip(i).ToList();
        }
        return [original[^1]];
    }
}
