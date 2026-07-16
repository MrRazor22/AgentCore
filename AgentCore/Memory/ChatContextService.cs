using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Memory;

internal sealed class ChatContextService : IContextService
{
    private readonly List<Message> _history = new();
    private readonly ITokenCounter _tokenCounter;
    private readonly IMemoryProvider _memoryProvider;
    private readonly ILLMProvider _llmProvider;
    private readonly double _compressionTarget;

    public ChatContextService(
        ITokenCounter tokenCounter,
        IMemoryProvider memoryProvider,
        ILLMProvider llmProvider,
        double compressionTarget = 0.70)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _memoryProvider = memoryProvider ?? throw new ArgumentNullException(nameof(memoryProvider));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        _compressionTarget = compressionTarget;
    }

    public async Task<List<Message>> PrepareConversationAsync(
        IContent? instructions,
        Message userInput,
        IReadOnlyList<Tool> tools,
        CancellationToken ct = default)
    {
        // 1. Recall long-term memory context
        string query = string.Join("\n", userInput.Contents.Select(c => c.ForLlm()));
        string recalledText = await _memoryProvider.RecallAsync(query, ct).ConfigureAwait(false);
        Message? memoryMessage = null;
        if (!string.IsNullOrWhiteSpace(recalledText))
        {
            memoryMessage = new Message(Role.System, new Text($"System:\nRecalled Context:\n{recalledText}"));
        }

        // 2. Measure tokens of fixed parts of the prompt
        var fixedMessages = new List<Message>();
        if (instructions != null)
        {
            fixedMessages.Add(new Message(Role.System, instructions));
        }
        if (memoryMessage != null)
        {
            fixedMessages.Add(memoryMessage);
        }
        fixedMessages.Add(userInput);

        // 3. Compute remaining budget for the rolling history using provider capabilities
        var capabilities = _llmProvider.GetCapabilities();
        int totalLimit = capabilities.ContextWindow;
        int fixedTokens = await _tokenCounter.EstimateAsync(fixedMessages, ct).ConfigureAwait(false);
        int toolTokens = tools.Count > 0 ? await _tokenCounter.EstimateAsync(tools, ct).ConfigureAwait(false) : 0;
        int budget = Math.Max(0, totalLimit - (fixedTokens + toolTokens + capabilities.ReservedTokens));

        List<Message> workingHistory;
        lock (_history)
        {
            workingHistory = _history.ToList();
        }

        int historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);

        // 4. Pure sliding window pruning (slice when budget exceeded)
        if (historyTokens > budget && budget > 0)
        {
            int targetLimit = (int)(budget * _compressionTarget);
            while (historyTokens > targetLimit && workingHistory.Count > 0)
            {
                workingHistory.RemoveAt(0); // prune oldest
                historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);
            }

            lock (_history)
            {
                _history.Clear();
                _history.AddRange(workingHistory);
            }
        }

        // 5. Construct conversation
        var conversation = new List<Message>();
        if (instructions != null)
        {
            conversation.Add(new Message(Role.System, instructions));
        }
        if (memoryMessage != null)
        {
            conversation.Add(memoryMessage);
        }
        conversation.AddRange(workingHistory);
        conversation.Add(userInput);

        return conversation;
    }

    public async Task UpdateHistoryAsync(
        IReadOnlyList<Message> completedTurn,
        CancellationToken ct = default)
    {
        if (completedTurn == null || completedTurn.Count == 0) return;

        lock (_history)
        {
            _history.AddRange(completedTurn);
        }

        // Track in long-term memory
        var serializedTurn = string.Join("\n", completedTurn.Select(m => $"{m.Role}: {string.Join("\n", m.Contents.Select(c => c.ForLlm()))}"));
        await _memoryProvider.RememberAsync(serializedTurn, ct).ConfigureAwait(false);
    }
}
