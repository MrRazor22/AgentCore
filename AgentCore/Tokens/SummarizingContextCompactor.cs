using AgentCore.Conversation;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Tokens;

public interface IContextCompactor
{
    Task<List<Message>> ReduceAsync(List<Message> chat, int totalTokens, LLMOptions options, CancellationToken ct = default);
}
public sealed class SummarizingContextCompactor : IContextCompactor
{
    private readonly ITokenCounter _counter;
    private readonly ILogger<SummarizingContextCompactor> _logger;
    private readonly ILLMProvider? _provider;
    private readonly string _summaryPrompt;

    public SummarizingContextCompactor(
        ITokenCounter counter,
        ILogger<SummarizingContextCompactor> logger,
        ILLMProvider? provider = null,
        string summaryPrompt = "Extract and summarize the core persistent facts, database credentials, specific user preferences, and prior tool results from this history. Create a concise scratchpad.")
    {
        _counter = counter;
        _logger = logger;
        _provider = provider;
        _summaryPrompt = summaryPrompt;
    }

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
            if ((chat[i].Kind & MessageKind.Summary) != 0)
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex > 0 && chat[startIndex - 1].Role == Role.User && (chat[startIndex - 1].Kind & MessageKind.Synthetic) != 0)
        {
            startIndex--;
        }

        int activeCount = chat.Count - startIndex;
        if (activeCount <= 4) return chat;

        int keepCount = Math.Min(4, activeCount - 1);
        int compressibleCount = activeCount - keepCount;

        int tailIndex = startIndex + compressibleCount;
        bool tailStartsWithUser = tailIndex < chat.Count && chat[tailIndex].Role == Role.User;

        var compressible = chat.Skip(startIndex).Take(compressibleCount).ToList();
        var contentLines = compressible.Select(m => $"[{m.Role}]: {string.Join(", ", m.Contents.Select(c => c.ForLlm()))}");
        
        // Cap summary input to ~60% of context to leave room for the prompt + output.
        // Use a rough 4 chars/token estimate for the cap.
        int maxInputTokens = (int)(ctxLen * 0.60);
        int maxChars = maxInputTokens * 4;

        var chunks = ChunkContent(contentLines, maxChars);
        var chunkSizes = chunks.Select(c => c.Length).ToList();
        _logger.LogInformation("Split context into {ChunkCount} chunks for parallel summarization. ChunkSizeRange: Min={Min} Max={Max} Avg={Avg}",
            chunks.Count, chunkSizes.Min(), chunkSizes.Max(), chunkSizes.Average());

        var tasks = chunks.Select((chunkText, index) => SummarizeChunkAsync(chunkText, options, ct)).ToList();
        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => !string.IsNullOrEmpty(r.Summary) || !string.IsNullOrEmpty(r.Reasoning));

        var combinedSummary = string.Join("\n\n", results.Select(r => r.Summary).Where(s => !string.IsNullOrEmpty(s)));
        var combinedReasoning = string.Join("\n\n", results.Select(r => r.Reasoning).Where(r => !string.IsNullOrEmpty(r)));

        if (string.IsNullOrEmpty(combinedSummary) && string.IsNullOrEmpty(combinedReasoning))
        {
            _logger.LogWarning("LLM returned empty summaries for all chunks. Context compaction aborted to prevent data loss.");
            return chat;
        }

        var contents = new List<IContent>();
        if (!string.IsNullOrEmpty(combinedSummary)) contents.Add(new Text(combinedSummary));
        if (!string.IsNullOrEmpty(combinedReasoning)) contents.Add(new Reasoning(combinedReasoning));

        chat.RemoveRange(startIndex, compressibleCount);
        
        chat.Insert(startIndex, new Message(Role.User, new Text("What have we done so far?"), MessageKind.Synthetic));
        chat.Insert(startIndex + 1, new Message(Role.Assistant, contents, MessageKind.Summary | MessageKind.Synthetic));

        if (!tailStartsWithUser)
        {
            chat.Insert(startIndex + 2, new Message(Role.User, new Text("Continue if you have a next step or stop and ask for clarification if you are unsure how to proceed."), MessageKind.Synthetic));
        }
        
        _logger.LogTrace("Summary content produced: SummaryLength={SumLen} ReasoningLength={ReasonLen} | Summary: {Summary} | Reasoning: {Reasoning}",
            combinedSummary.Length, combinedReasoning.Length,
            combinedSummary.Length > 200 ? combinedSummary[..200] + "..." : combinedSummary,
            combinedReasoning.Length > 200 ? combinedReasoning[..200] + "..." : combinedReasoning);

        int after = await _counter.CountAsync(chat.GetActiveWindow(), ct).ConfigureAwait(false);
        _logger.LogDebug("Compacted [summarize]: {Before}→{After} ({Saved:P0})",
            totalTokens, after, 1.0 - (double)after / totalTokens);

        return chat;
    }

    private static List<string> ChunkContent(IEnumerable<string> lines, int maxChars)
    {
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var line in lines)
        {
            if (currentChunk.Length + line.Length > maxChars && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }

            // If a single line is enormous, it will exceed maxChars but at least we process it
            currentChunk.AppendLine(line);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    private async Task<(string Summary, string Reasoning)> SummarizeChunkAsync(string text, LLMOptions options, CancellationToken ct)
    {
        if (_provider == null) return ("", "");

        var input = new List<Message> { 
            new(Role.System, new Text(_summaryPrompt)),
            new(Role.User, new Text(text))
        };

        var textSb = new StringBuilder();
        var reasonSb = new StringBuilder();

        try
        {
            await foreach (var evt in _provider.StreamAsync(input, new LLMOptions { Model = options.Model }, null, ct))
            {
                if (evt is TextDelta td) textSb.Append(td.Value);
                else if (evt is ReasoningDelta rd) reasonSb.Append(rd.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize context chunk.");
            return ("", "");
        }

        return (textSb.ToString().Trim(), reasonSb.ToString().Trim());
    }
}
