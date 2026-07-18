using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Memory;

public interface IContextService
{
    Task<List<Message>> PrepareAsync(IContent? instructions, Message userInput, IReadOnlyList<Tool> tools, CancellationToken ct = default);
    Task UpdateAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default);
}

internal sealed class ContextService : IContextService
{
    private readonly List<Message> _history = new();
    private readonly ITokenCounter _tokenCounter;
    private readonly IMemory _memoryProvider;
    private readonly ILLM _llmProvider;
    private readonly double _compressionTarget;

    public ContextService(
        ITokenCounter tokenCounter,
        IMemory memoryProvider,
        ILLM llmProvider,
        double compressionTarget = 0.70)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _memoryProvider = memoryProvider ?? throw new ArgumentNullException(nameof(memoryProvider));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        _compressionTarget = compressionTarget;
    }

    public async Task<List<Message>> PrepareAsync(
        IContent? instructions,
        Message userInput,
        IReadOnlyList<Tool> tools,
        CancellationToken ct = default)
    {
        // 1. Recall long-term memory context
        string query = string.Join("\n", userInput.Contents.Select(c => c.ForLlm()));
        IContent recalled = await _memoryProvider.RecallAsync(new Text(query), ct).ConfigureAwait(false);
        Message preparedUserInput = userInput;
        if (recalled != null && !string.IsNullOrWhiteSpace(recalled.ForLlm()))
        {
            var userContents = new List<IContent>
            {
                new Text("<retrieved_context>"),
                recalled,
                new Text("</retrieved_context>"),
                new Text("<query>"),
                new Text(query),
                new Text("</query>")
            };

            preparedUserInput = new Message(Role.User, userContents);
        }

        // 2. Measure tokens of fixed parts of the prompt
        var fixedMessages = new List<Message>();
        if (instructions != null) fixedMessages.Add(new Message(Role.System, instructions));
        fixedMessages.Add(preparedUserInput);

        // 3. Compute remaining budget for rolling history
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

        // Prune the local copy to fit the exact final budget (no mutation to master history)
        if (historyTokens > budget)
        {
            int targetLimit = (int)(budget * _compressionTarget);
            while (historyTokens > targetLimit && workingHistory.Count > 0)
            {
                workingHistory.RemoveAt(0); // prune oldest
                historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);
            }
        }

        // 4. Construct conversation
        var conversation = new List<Message>();
        if (instructions != null) conversation.Add(new Message(Role.System, instructions));
        conversation.AddRange(workingHistory);
        conversation.Add(preparedUserInput);

        return conversation;
    }

    public async Task UpdateAsync(
        IReadOnlyList<Message> completedTurn,
        CancellationToken ct = default)
    {
        if (completedTurn == null || completedTurn.Count == 0) return;

        lock (_history)
        {
            _history.AddRange(completedTurn);
        }

        // Maintain bounded working context (limit history size based on model capacity)
        var capabilities = _llmProvider.GetCapabilities();
        int maxCapacity = Math.Max(0, capabilities.ContextWindow - capabilities.ReservedTokens);

        List<Message> workingHistory;
        lock (_history)
        {
            workingHistory = _history.ToList();
        }

        int historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);

        if (historyTokens > maxCapacity)
        {
            int targetLimit = (int)(maxCapacity * _compressionTarget);
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

        // Always save to memory provider to support real-time engines like Mem0
        await _memoryProvider.RememberAsync(completedTurn, ct).ConfigureAwait(false);
    }
}
