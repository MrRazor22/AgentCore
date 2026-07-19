using AgentCore.LLM;
using AgentCore.LLM.Chat;
using System.Text;

using System.Collections.Concurrent;

namespace AgentCore.Memory;

public interface IMemory
{
    Task RememberAsync(IReadOnlyList<Message> turn, CancellationToken ct = default);

    /// <summary>
    /// Recalls consolidated context information (e.g., factsheets or buffer aggregations)
    /// to inject into the agent's chat context. 
    /// Note: This does not guarantee or require semantic query/similarity-search relevance;
    /// the implementation determines how the stored memory content is synthesized or retrieved.
    /// </summary>
    Task<IContent> RecallAsync(IContent query, CancellationToken ct = default);
}

public class SummarizerMemory : IMemory
{
    private readonly ILLMService? _llmService;
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMCapabilities? _capabilities;
    private readonly List<Message> _buffer = new();
    private string _factSheet = string.Empty;
    private readonly double _compactionFraction;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SummarizerMemory(
        ILLMService? llmService = null,
        ITokenCounter? tokenCounter = null,
        LLMCapabilities? capabilities = null,
        double compactionFraction = 0.3)
    {
        _llmService = llmService;
        _tokenCounter = tokenCounter ?? new ApproximateTokenCounter();
        _capabilities = capabilities;
        _compactionFraction = compactionFraction;
    }

    public async Task RememberAsync(IReadOnlyList<Message> turn, CancellationToken ct = default)
    {
        if (turn == null || turn.Count == 0) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // If there's no LLM Service configured, act as a fallback in-memory store.
            if (_llmService == null || _capabilities == null)
            {
                _buffer.AddRange(turn);
                return;
            }

            int totalLimit = _capabilities.ContextWindow;
            int reserved = _capabilities.ReservedTokens;

            // Base overhead for prompt template instructions in consolidation
            const int promptOverhead = 300;
            int safeBudget = totalLimit - reserved;

            // 1. Estimate incoming turn size. If it exceeds safe consolidation budget, run Targeted Truncation.
            int incomingTokens = await _tokenCounter.EstimateAsync(turn, ct).ConfigureAwait(false);

            IReadOnlyList<Message> finalizedTurn = turn;
            if (incomingTokens + promptOverhead > safeBudget)
            {
                finalizedTurn = await PerformTargetedTruncationAsync(turn, safeBudget - promptOverhead, ct).ConfigureAwait(false);
                incomingTokens = await _tokenCounter.EstimateAsync(finalizedTurn, ct).ConfigureAwait(false);
            }

            // 2. Add turn to accumulation buffer
            _buffer.AddRange(finalizedTurn);

            // 3. Mathematical Compaction Trigger: check size of raw accumulated buffer only (excluding factSheet size)
            int bufferTokens = await _tokenCounter.EstimateAsync(_buffer, ct).ConfigureAwait(false);
            int triggerThreshold = (int)(safeBudget * _compactionFraction);

            if (bufferTokens > triggerThreshold)
            {
                // Verify Consolidation safety:
                // combined size of existingFactSheet + buffer + promptOverhead must fit in safe budget.
                int existingSummaryTokens = string.IsNullOrEmpty(_factSheet) ? 0 :
                    await _tokenCounter.EstimateAsync(new[] { new Message(Role.User, new Text(_factSheet)) }, ct).ConfigureAwait(false);

                if (existingSummaryTokens + bufferTokens + promptOverhead > safeBudget)
                {
                    // Target truncate the accumulation buffer to fit the remaining space
                    int maxAllowedBufferTokens = Math.Max(100, safeBudget - promptOverhead - existingSummaryTokens);
                    var truncatedBuffer = await PerformTargetedTruncationAsync(_buffer, maxAllowedBufferTokens, ct).ConfigureAwait(false);
                    
                    _buffer.Clear();
                    _buffer.AddRange(truncatedBuffer);
                }

                // Consolidate buffered turns + existing factSheet into a distilled fact sheet
                _factSheet = await ConsolidateAsync(_factSheet, _buffer, ct).ConfigureAwait(false);
                _buffer.Clear();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IContent> RecallAsync(IContent query, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_llmService == null)
            {
                // In fallback mode, return a formatted text representation of all accumulated turns
                if (_buffer.Count == 0) return new Text(string.Empty);
                var sb = new StringBuilder();
                foreach (var msg in _buffer)
                {
                    sb.AppendLine($"{msg.Role}: {string.Join("\n", msg.Contents.Select(c => c.ForLlm()))}");
                }
                return new Text(sb.ToString().Trim());
            }

            return new Text(_factSheet);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<Message>> PerformTargetedTruncationAsync(IReadOnlyList<Message> turn, int targetTokenLimit, CancellationToken ct)
    {
        var messages = turn.Select(m => new Message(m.Role, m.Contents.ToList())).ToList();

        // 1. Locate all ToolResults in the messages and recursively truncate them first
        foreach (var message in messages)
        {
            var truncatedContents = new List<IContent>();
            foreach (var content in message.Contents)
            {
                if (content is ToolResult tr && tr.Result is Text textResult && !string.IsNullOrEmpty(textResult.Value))
                {
                    // Truncate ToolResult value to a safe length
                    // Using character heuristic as a safe fast method: 1 token ~ 4 chars
                    int limitChars = Math.Max(100, targetTokenLimit * 4 / messages.Count);
                    if (textResult.Value.Length > limitChars)
                    {
                        var newValue = textResult.Value[..limitChars] + "\n... [truncated due to context limits]";
                        truncatedContents.Add(new ToolResult(tr.CallId, new Text(newValue)));
                        continue;
                    }
                }
                truncatedContents.Add(content);
            }
            message.Contents = truncatedContents;
        }

        // Verify size after ToolResult truncation
        int estimated = await _tokenCounter.EstimateAsync(messages, ct).ConfigureAwait(false);
        if (estimated <= targetTokenLimit)
        {
            return messages;
        }

        // 2. Generic fallback: if still too large, truncate other Assistant text contents or any large content
        foreach (var message in messages)
        {
            if (message.Role == Role.Assistant)
            {
                var truncatedContents = new List<IContent>();
                foreach (var content in message.Contents)
                {
                    if (content is Text t && !string.IsNullOrEmpty(t.Value))
                    {
                        int limitChars = Math.Max(100, targetTokenLimit * 4 / messages.Count);
                        if (t.Value.Length > limitChars)
                        {
                            var newValue = t.Value[..limitChars] + "\n... [truncated due to context limits]";
                            truncatedContents.Add(new Text(newValue));
                            continue;
                        }
                    }
                    truncatedContents.Add(content);
                }
                message.Contents = truncatedContents;
            }
        }

        return messages;
    }

    private async Task<string> ConsolidateAsync(string existingFactSheet, List<Message> turns, CancellationToken ct)
    {
        var sbTurns = new StringBuilder();
        foreach (var turn in turns)
        {
            sbTurns.AppendLine($"{turn.Role}: {string.Join("\n", turn.Contents.Select(c => c.ForLlm()))}");
        }

        var prompt = new Message(Role.System, new Text(
            "You are a memory consolidation assistant. Your task is to update the existing distilled fact sheet with new conversation turns. Add new facts, preference profiles, and user details, resolve any logical conflicts, and remove outdated instructions. Do not lose critical context. Keep the fact sheet concise, bulleted, and structured. Do not output conversational responses or logs; only output the updated fact sheet."));

        var userContext = new Message(Role.User, new Text(
            $"Existing Fact Sheet:\n{existingFactSheet}\n\nNew Conversation Turns:\n{sbTurns}"));

        var messages = new List<Message> { prompt, userContext };
        var sb = new StringBuilder();

        await foreach (var evt in _llmService!.StreamAsync(messages, options: null, tools: null, ct: ct).ConfigureAwait(false))
        {
            if (evt is Text t)
            {
                sb.Append(t.Value);
            }
        }

        return sb.ToString().Trim();
    }
}