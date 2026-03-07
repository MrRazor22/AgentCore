using AgentCore.Chat;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Tokens;

public sealed class SummarizingContextManager(
    ITokenCounter _counter,
    ILogger<SummarizingContextManager> _logger,
    ILLMProvider? _provider = null,
    string summaryPrompt = "Extract and summarize the core persistent facts, database credentials, specific user preferences, and prior tool results from this history. Create a concise scratchpad.",
    int keepLastMessages = 10
) : IContextManager
{
    public async Task<IList<Message>> ReduceAsync(IList<Message> messages, LLMOptions options, CancellationToken ct = default)
    {
        if (messages == null) throw new ArgumentNullException(nameof(messages));

        int contextLength = options.ContextLength
            ?? throw new InvalidOperationException("ContextLength is required. Set it in LLMOptions or via provider.");

        int reserveForOutput = options.MaxOutputTokens ?? (int)Math.Min(4096, contextLength * 0.25);
        int available = contextLength - reserveForOutput;

        if (available <= 0)
            throw new InvalidOperationException($"No available context budget: ContextLength={contextLength}, MaxOutputTokens={reserveForOutput}.");

        int current = await _counter.CountAsync(messages, ct).ConfigureAwait(false);
        if (current <= available) return messages;

        var source = messages.Clone();
        var system = source.Where(m => m.Role == Role.System).ToList();
        var history = source.Where(m => m.Role != Role.System).ToList();

        var keepHistory = history.Skip(Math.Max(0, history.Count - keepLastMessages)).ToList();

        IList<Message> Build(int skipCount, string? summaryText)
        {
            while (skipCount < keepHistory.Count)
            {
                var m = keepHistory[skipCount];
                if (m.Role == Role.Tool) { skipCount++; continue; }
                break;
            }

            var result = new List<Message>();
            foreach (var s in system) result.Add(new Message(s.Role, s.Content));
            
            if (!string.IsNullOrWhiteSpace(summaryText))
                result.Add(new Message(Role.Assistant, new Text($"[Prior Context Scratchpad]:\n{summaryText}")));
                
            for (int i = skipCount; i < keepHistory.Count; i++) result.Add(new Message(keepHistory[i].Role, keepHistory[i].Content));
            return result;
        }

        int lo = 0, hi = keepHistory.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            var candidate = Build(mid, null);
            if (await _counter.CountAsync(candidate, ct).ConfigureAwait(false) <= available)
                hi = mid;
            else
                lo = mid + 1;
        }

        int skipFromKeep = lo;
        var totallyDropped = history.Take(history.Count - keepLastMessages + skipFromKeep).ToList();

        string? summary = null;
        if (_provider != null && totallyDropped.Count > 0)
        {
            var summaryReq = new List<Message> 
            { 
               new Message(Role.System, new Text(summaryPrompt)),
               new Message(Role.User, new Text(string.Join("\n\n", totallyDropped.Select(m => $"[{m.Role}]: {m.Content}"))))
            };
            
            var sumOptions = new LLMOptions { Model = options.Model, MaxOutputTokens = 1024, ContextLength = options.ContextLength };
            var stream = _provider.StreamAsync(summaryReq, sumOptions, null, ct);
            
            var sb = new StringBuilder();
            await foreach(var evt in stream.WithCancellation(ct))
            {
               if (evt is TextDelta td) sb.Append(td.Value);
            }
            summary = sb.ToString().Trim();
        }

        var rebuilt = Build(lo, summary);
        
        int tokens = await _counter.CountAsync(rebuilt, ct).ConfigureAwait(false);
        if (tokens > available)
        {
            _logger.LogWarning("Summary caused token overflow. Falling back to tail-trim.");
            rebuilt = Build(lo, null);
            tokens = await _counter.CountAsync(rebuilt, ct).ConfigureAwait(false);
        }

        if (summary != null)
            _logger.LogWarning("Context Overflow handled: Summarized {Dropped} skipped messages into context memory. Tokens: {Current} -> {Final}.", totallyDropped.Count, current, tokens);
        else
            _logger.LogWarning("Context Overflow handled: Tail-trimmed conversation history to {Tokens} tokens.", tokens);

        return rebuilt;
    }
}
