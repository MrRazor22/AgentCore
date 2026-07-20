using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.Memory;

public interface IMemory
{
    Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default);
    Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default);
}

public class RollingWindowMemory : IMemory
{
    private readonly List<Message> _history = new();
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMCapabilities _capabilities;
    private readonly IReadOnlyList<Tool> _tools;
    private readonly IContent? _instructions;
    private readonly double _retentionTarget;

    public RollingWindowMemory(
        ITokenCounter tokenCounter,
        LLMCapabilities capabilities,
        IReadOnlyList<Tool> tools,
        IContent? instructions,
        double retentionTarget = 0.70)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _instructions = instructions;
        _retentionTarget = retentionTarget;
    }

    public async Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default)
    {
        // 1. Measure tokens of fixed parts of the prompt
        var fixedMessages = new List<Message>();
        if (_instructions != null) fixedMessages.Add(new Message(Role.System, _instructions));
        fixedMessages.Add(newInput);

        // 2. Compute remaining budget for rolling history
        int totalLimit = _capabilities.ContextWindow;
        int fixedTokens = await _tokenCounter.EstimateAsync(fixedMessages, ct).ConfigureAwait(false);
        int toolTokens = _tools.Count > 0 ? await _tokenCounter.EstimateAsync(_tools, ct).ConfigureAwait(false) : 0;
        int budget = Math.Max(0, totalLimit - (fixedTokens + toolTokens + _capabilities.ReservedTokens));

        List<Message> workingHistory;
        lock (_history)
        {
            workingHistory = _history.ToList();
        }

        int historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);

        // Prune the local copy to fit the exact final budget (no mutation to master history)
        if (historyTokens > budget)
        {
            int targetLimit = (int)(budget * _retentionTarget);
            while (historyTokens > targetLimit && workingHistory.Count > 0)
            {
                workingHistory.RemoveAt(0); // prune oldest
                historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);
            }
        }

        // 3. Construct conversation
        var conversation = new List<Message>();
        if (_instructions != null) conversation.Add(new Message(Role.System, _instructions));
        conversation.AddRange(workingHistory);
        conversation.Add(newInput);

        return conversation;
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        if (completedTurn == null || completedTurn.Count == 0) return;

        lock (_history)
        {
            _history.AddRange(completedTurn);
        }

        // Maintain bounded working context (limit history size based on model capacity)
        int maxCapacity = Math.Max(0, _capabilities.ContextWindow - _capabilities.ReservedTokens);

        List<Message> workingHistory;
        lock (_history)
        {
            workingHistory = _history.ToList();
        }

        int historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);

        if (historyTokens > maxCapacity)
        {
            int targetLimit = (int)(maxCapacity * _retentionTarget);
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
    }
}

