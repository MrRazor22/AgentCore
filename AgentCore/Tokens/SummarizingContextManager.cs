using AgentCore.Conversation;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Tokens;

public sealed class SummarizingContextManager(
    ITokenCounter _counter,
    ILogger<SummarizingContextManager> _logger,
    ILLMProvider? _provider = null,
    string summaryPrompt = "Extract and summarize the core persistent facts, database credentials, specific user preferences, and prior tool results from this history. Create a concise scratchpad."
) : IContextManager
{
    private readonly string _summaryPrompt = summaryPrompt;

    public async Task<List<Message>> ReduceAsync(List<Message> chat, int totalTokens, LLMOptions options, CancellationToken ct = default)
    {
        int ctxLen = options.ContextLength ?? throw new InvalidOperationException("ContextLength is required.");
        double usage = ctxLen > 0 && totalTokens > 0 ? (double)totalTokens / ctxLen : 0;
        _logger.LogDebug("Context check: {Used}/{Ctx} ({Usage:P0})", totalTokens, ctxLen, usage);

        if (usage < 0.75 || _provider == null) return chat;

        _logger.LogInformation("Context compaction triggered: {Used}/{Limit} ({Pct:F1}%)", totalTokens, ctxLen, usage * 100);

        int startIndex = 0;
        for (int i = chat.Count - 1; i >= 0; i--)
        {
            if (chat[i].Contents.Any(c => c is Summary))
            {
                startIndex = i;
                break;
            }
        }

        int activeCount = chat.Count - startIndex;
        if (activeCount <= 4) return chat; // Too few messages to summarize

        int keepCount = Math.Min(4, activeCount - 1);
        int compressibleCount = activeCount - keepCount;

        var compressible = chat.Skip(startIndex).Take(compressibleCount).ToList();
        var contentLines = compressible.Select(m => $"[{m.Role}]: {string.Join(", ", m.Contents.Select(c => c.ForLlm()))}");
        var contentStr = string.Join("\n\n", contentLines);

        // Cap summary input to ~60% of context to leave room for the prompt + output.
        // Use a rough 4 chars/token estimate for the cap.
        int maxInputTokens = (int)(ctxLen * 0.60);
        int maxChars = maxInputTokens * 4;
        if (contentStr.Length > maxChars)
        {
            _logger.LogDebug("Summary input truncated: {Original} chars → {Capped} chars to fit context", contentStr.Length, maxChars);
            contentStr = contentStr[..maxChars];
        }

        var input = new List<Message> { 
            new(Role.System, new Text(_summaryPrompt)),
            new(Role.User, new Text(contentStr))
        };

        var textSb = new StringBuilder();
        var reasonSb = new StringBuilder();

        // Summary call: no tools, no hardcoded constraints. Let it output until it hits a natural stop token
        // bounded only by the remaining 40% of the context window buffer.
        await foreach (var evt in _provider.StreamAsync(input,
            new LLMOptions { Model = options.Model },
            null, ct))
        {
            if (evt is TextDelta td) textSb.Append(td.Value);
            else if (evt is ReasoningDelta rd) reasonSb.Append(rd.Value);
        }

        var summaryContent = textSb.ToString().Trim();
        var reasonContent = reasonSb.ToString().Trim();

        if (string.IsNullOrEmpty(summaryContent) && string.IsNullOrEmpty(reasonContent))
        {
            _logger.LogWarning("LLM returned empty summary. Context compaction aborted to prevent data loss.");
            return chat;
        }

        var contents = new List<IContent>();
        if (!string.IsNullOrEmpty(reasonContent)) contents.Add(new Reasoning(reasonContent));
        if (!string.IsNullOrEmpty(summaryContent)) contents.Add(new Summary(summaryContent));

        var summaryMsg = new Message(Role.Assistant, contents.ToArray());
        chat.RemoveRange(startIndex, compressibleCount);
        chat.Insert(startIndex, summaryMsg);
        
        _logger.LogTrace("Summary content produced: {Summary} | Reasoning: {Reasoning}", 
            summaryContent.Length > 200 ? summaryContent[..200] + "..." : summaryContent,
            reasonContent.Length > 200 ? reasonContent[..200] + "..." : reasonContent);

        int after = await _counter.CountAsync(chat.GetActiveWindow(), ct).ConfigureAwait(false);
        _logger.LogDebug("Compacted [summarize]: {Before}→{After} ({Saved:P0})",
            totalTokens, after, 1.0 - (double)after / totalTokens);

        return chat;
    }
}
