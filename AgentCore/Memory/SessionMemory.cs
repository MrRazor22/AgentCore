using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tokens;

namespace AgentCore.Memory;

public sealed class SessionMemoryOptions
{
    public double CompressionTarget { get; set; } = 0.70;
    public int MinRecentTokens { get; set; } = 2000;
}

public sealed class SessionMemory : IMemory
{
    private readonly ConcurrentDictionary<string, List<Message>> _store = new();
    private readonly ITokenCounter _tokenCounter;
    private readonly ILLMProvider _llmProvider;
    private readonly SessionMemoryOptions _options;

    public SessionMemory(
        ITokenCounter tokenCounter,
        ILLMProvider llmProvider,
        SessionMemoryOptions? options = null)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        _options = options ?? new SessionMemoryOptions();
    }

    public async Task<IReadOnlyList<Message>> RecallAsync(
        string sessionId,
        Message currentInput,
        TokenBudget budget,
        CancellationToken ct = default)
    {
        var history = _store.GetOrAdd(sessionId, _ => new List<Message>());
        List<Message> workingHistory;
        lock (history)
        {
            workingHistory = history.ToList();
        }

        // Measure total tokens of current history + current input
        int currentInputTokens = await _tokenCounter.CountAsync(new[] { currentInput }, ct).ConfigureAwait(false);
        int historyTokens = await _tokenCounter.CountAsync(workingHistory, ct).ConfigureAwait(false);
        int totalTokens = currentInputTokens + historyTokens;

        int maxLimit = budget.MaxTokens;

        // If context budget is 0 or unconfigured, we do not summarize, just return history
        if (maxLimit <= 0)
        {
            return workingHistory;
        }

        // Summarize only when totalTokens exceeds the budget
        if (totalTokens > maxLimit)
        {
            int targetLimit = (int)(maxLimit * _options.CompressionTarget);
            bool historyModified = false;

            // Iteratively compress the oldest messages
            while (totalTokens > targetLimit)
            {
                // Measure total tokens of history we could potentially summarize
                // We must preserve at least MinRecentTokens of unsummarized recent conversation
                int unsummarizedTokens = 0;
                int messagesToKeep = 0;

                // Traverse backwards from the end to find the boundary of MinRecentTokens
                for (int i = workingHistory.Count - 1; i >= 0; i--)
                {
                    var msg = workingHistory[i];
                    // Skip existing system summary messages in our verification of recent conversation
                    if (msg.Role == Role.System && msg.Contents.Any(c => c.ForLlm().StartsWith("System:\nConversation Summary:")))
                    {
                        break;
                    }

                    int msgTokens = await _tokenCounter.CountAsync(new[] { msg }, ct).ConfigureAwait(false);
                    unsummarizedTokens += msgTokens;
                    messagesToKeep++;

                    if (unsummarizedTokens >= _options.MinRecentTokens)
                    {
                        break;
                    }
                }

                // If the entire unsummarized history is less than or equal to MinRecentTokens,
                // we stop summarizing and fallback to trimming the oldest message.
                int summarizableCount = workingHistory.Count - messagesToKeep;
                if (summarizableCount <= 0)
                {
                    // Fallback to trimming the oldest message to prevent infinite compression of a tiny summary
                    if (workingHistory.Count > 0)
                    {
                        workingHistory.RemoveAt(0);
                        historyModified = true;
                        historyTokens = await _tokenCounter.CountAsync(workingHistory, ct).ConfigureAwait(false);
                        totalTokens = currentInputTokens + historyTokens;
                    }
                    else
                    {
                        break;
                    }
                    continue;
                }

                // Dynamically size the chunk to summarize
                int dynamicChunkSize = Math.Min(2000, maxLimit / 4);
                
                // Grab the oldest messages that fit within the chunk size
                List<Message> chunkToSummarize = [];
                int chunkTokens = 0;
                for (int i = 0; i < summarizableCount; i++)
                {
                    var msg = workingHistory[i];
                    int msgTokens = await _tokenCounter.CountAsync(new[] { msg }, ct).ConfigureAwait(false);
                    
                    if (chunkTokens + msgTokens > dynamicChunkSize && chunkToSummarize.Count > 0)
                    {
                        break;
                    }

                    chunkToSummarize.Add(msg);
                    chunkTokens += msgTokens;
                }

                if (chunkToSummarize.Count == 0)
                {
                    break;
                }

                // Call LLM provider to summarize this chunk
                string summaryText = await SummarizeChunkAsync(chunkToSummarize, ct).ConfigureAwait(false);

                // Replace the chunk in history with a single System message containing the summary
                var summaryMessage = new Message(Role.System, new Text($"System:\nConversation Summary:\n{summaryText}"));
                
                workingHistory.RemoveRange(0, chunkToSummarize.Count);
                workingHistory.Insert(0, summaryMessage);
                historyModified = true;

                // Re-evaluate total tokens
                historyTokens = await _tokenCounter.CountAsync(workingHistory, ct).ConfigureAwait(false);
                totalTokens = currentInputTokens + historyTokens;
            }

            if (historyModified)
            {
                lock (history)
                {
                    history.Clear();
                    history.AddRange(workingHistory);
                }
            }
        }

        return workingHistory;
    }

    public async Task RememberAsync(
        string sessionId,
        IReadOnlyList<Message> completedTurn,
        CancellationToken ct = default)
    {
        if (completedTurn == null || completedTurn.Count == 0) return;

        var history = _store.GetOrAdd(sessionId, _ => new List<Message>());
        lock (history)
        {
            history.AddRange(completedTurn);
        }
        await Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private async Task<string> SummarizeChunkAsync(IReadOnlyList<Message> chunk, CancellationToken ct)
    {
        var summaryPrompt = new Message(Role.System, new Text(
            "You are a memory compressor. Summarize the following early conversation history chunk into a single concise summary message of key facts, instructions, results, and progress. Focus only on persistent information worth retaining. Keep it brief."));

        var messages = new List<Message> { summaryPrompt };
        messages.AddRange(chunk);

        var options = new LLMOptions
        {
            ToolCallMode = ToolCallMode.None,
            Temperature = 0.2f
        };

        var sb = new StringBuilder();
        await foreach (var delta in _llmProvider.StreamAsync(messages, options, tools: null, ct).ConfigureAwait(false))
        {
            if (delta is TextDelta td) sb.Append(td.Value);
        }
        return sb.ToString().Trim();
    }
}
