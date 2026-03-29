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

        if (usage < 0.90 || _provider == null) return chat;

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
        var content = compressible.Select(m => $"[{m.Role}]: {string.Join(", ", m.Contents.Select(c => c.ForLlm()))}");

        var input = new List<Message> { 
            new(Role.System, new Text(_summaryPrompt)),
            new(Role.User, new Text(string.Join("\n\n", content)))
        };

        var sb = new StringBuilder();
        await foreach (var evt in _provider.StreamAsync(input,
            new LLMOptions { Model = options.Model, MaxOutputTokens = 1024, ContextLength = options.ContextLength },
            null, ct))
        {
            if (evt is TextDelta td) sb.Append(td.Value);
        }

        var summaryMsg = new Message(Role.System, new Summary(sb.ToString().Trim()));
        int insertIndex = chat.Count - keepCount;
        chat.Insert(insertIndex, summaryMsg);

        int after = await _counter.CountAsync(chat.GetActiveWindow(), ct).ConfigureAwait(false);
        _logger.LogDebug("Compacted [summarize]: {Before}→{After} ({Saved:P0})",
            totalTokens, after, 1.0 - (double)after / totalTokens);

        return chat;
    }
}
