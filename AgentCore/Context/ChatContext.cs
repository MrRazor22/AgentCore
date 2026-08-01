using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Context;

public class ChatContext : IContext
{
    private readonly List<Message> _chat = new();
    private List<Message>? _pendingPrompt;
    private readonly ILLM? _summarizer;
    private readonly ILogger<ChatContext>? _logger;

    private readonly int _contextWindow;
    private readonly int _reserveTokens;

    private double _charsPerToken = 5.0;
    private const double EmaAlpha = 0.1;
    private const double SafetyMargin = 1.15;

    public int TokenUsage { get; private set; }

    public IReadOnlyList<Message> Messages
    {
        get
        {
            lock (_chat)
            {
                return _chat.ToList();
            }
        }
    }

    public ChatContext(
        int contextWindow,
        int? reserveTokens = null,
        ILLM? summarizer = null,
        ILogger<ChatContext>? logger = null)
    {
        _contextWindow = contextWindow;
        _reserveTokens = reserveTokens ?? (contextWindow / 10);
        _summarizer = summarizer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Message>> BuildPromptAsync(
        IReadOnlyList<Message> uncommittedMessages,
        CancellationToken ct = default)
    {
        if (uncommittedMessages == null)
        {
            uncommittedMessages = Array.Empty<Message>();
        }

        int uncommittedEstimate = uncommittedMessages.Sum(m => Estimate(m));
        int estimatedTotal = TokenUsage + uncommittedEstimate;
        int limit = _contextWindow - _reserveTokens;

        if (estimatedTotal > limit)
        {
            _logger?.LogInformation("Compaction triggered. EstimatedTotal={Count}, Limit={Limit}", estimatedTotal, limit);
            await CompactHistoryIfNeededAsync(ct).ConfigureAwait(false);
        }

        lock (_chat)
        {
            var preparedPrompt = new List<Message>(_chat);
            preparedPrompt.AddRange(uncommittedMessages);
            _pendingPrompt = preparedPrompt;
            return preparedPrompt;
        }
    }

    public Task CommitAsync(
        TokenUsage usage,
        IReadOnlyList<Message> response,
        CancellationToken ct = default)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (usage == null) throw new ArgumentNullException(nameof(usage));

        lock (_chat)
        {
            var prompt = _pendingPrompt ?? _chat.ToList();
            _chat.Clear();
            _chat.AddRange(prompt);
            _chat.AddRange(response);

            TokenUsage = usage.InputTokens + usage.OutputTokens;

            // Calibrate our charsPerToken using the actual prompt tokens
            int promptChars = GetCharacterCount(prompt);
            if (promptChars > 0 && usage.InputTokens > 0)
            {
                double currentRatio = (double)promptChars / usage.InputTokens;
                if (currentRatio >= 1.0 && currentRatio <= 10.0)
                {
                    _charsPerToken = EmaAlpha * currentRatio + (1 - EmaAlpha) * _charsPerToken;
                    _logger?.LogDebug("Token calibration updated: CharsPerToken={Ratio}", _charsPerToken);
                }
            }

            _pendingPrompt = null;
        }

        return Task.CompletedTask;
    }

    private async Task CompactHistoryIfNeededAsync(CancellationToken ct = default)
    {
        if (_summarizer != null)
        {
            List<Message> chatToCompact;
            lock (_chat)
            {
                chatToCompact = _chat.ToList();
            }

            var systemMessage = chatToCompact.FirstOrDefault(m => m.Role == Role.System);
            string summary = await ConsolidateAsync(chatToCompact, ct).ConfigureAwait(false);

            lock (_chat)
            {
                _chat.Clear();
                
                var summaryMessage = new Message(Role.User, new CompactedSummary(summary));
                _chat.AddIfValid(systemMessage)
                     .AddIfValid(summaryMessage);

                // Reset token count to estimate of System instructions + the summary
                int newCount = 0;
                if (systemMessage != null)
                {
                    newCount += Estimate(systemMessage);
                }
                newCount += Estimate(summaryMessage);
                TokenUsage = newCount;
            }
        }
        else
        {
            // Fallback: Rolling window trimming
            lock (_chat)
            {
                // Start checking from the first message after the system prompt (if one exists)
                bool hasSystem = _chat.Any(m => m.Role == Role.System);
                int startIndex = hasSystem ? 1 : 0;
                while (_chat.Count > (startIndex + 1))
                {
                    var evicted = _chat[startIndex];
                    _chat.RemoveAt(startIndex);
                    TokenUsage -= Estimate(evicted);
                }
            }
        }
    }

    private async Task<string> ConsolidateAsync(List<Message> turns, CancellationToken ct)
    {
        var sbTurns = new StringBuilder();
        foreach (var turn in turns)
        {
            sbTurns.AppendLine($"{turn.Role}: {string.Join("\n", turn.Contents.Select(c => c.ForLlm()))}");
        }

        var prompt = new Message(Role.System, new Text(
            "You are a memory consolidation assistant. Your task is to update the existing distilled fact sheet with new conversation turns. Add new facts, preference profiles, and user details, resolve any logical conflicts, and remove outdated instructions. Do not lose critical context. Keep the fact sheet concise, bulleted, and structured. Do not output conversational responses or logs; only output the updated fact sheet."));

        var userContext = new Message(Role.User, new Text(
            $"Conversation Turns to Summarize:\n{sbTurns}"));

        var messages = new List<Message> { prompt, userContext };
        var sb = new StringBuilder();

        await foreach (var evt in _summarizer!.StreamAsync(messages, options: null, tools: null, ct: ct).ConfigureAwait(false))
        {
            if (evt is TextDelta t)
            {
                sb.Append(t.Value);
            }
        }

        return sb.ToString().Trim();
    }

    private int Estimate(Message message)
    {
        int chars = 4; // overhead
        foreach (var content in message.Contents)
        {
            chars += content switch
            {
                Text t => t.Value?.Length ?? 0,
                CompactedSummary cs => cs.Summary?.Length ?? 0,
                ToolCall tc => tc.Name.Length + (tc.Arguments?.ToString()?.Length ?? 0),
                ToolResult tr => tr.Result?.ForLlm()?.Length ?? 0,
                _ => 0
            };
        }
        return (int)((chars / _charsPerToken) * SafetyMargin);
    }

    private int GetCharacterCount(IEnumerable<Message> messages)
    {
        int total = 0;
        foreach (var message in messages)
        {
            total += EstimateMessageCharacters(message);
        }
        return total;
    }

    private int EstimateMessageCharacters(Message message)
    {
        int chars = 4; // overhead
        foreach (var content in message.Contents)
        {
            chars += content switch
            {
                Text t => t.Value?.Length ?? 0,
                CompactedSummary cs => cs.Summary?.Length ?? 0,
                ToolCall tc => tc.Name.Length + (tc.Arguments?.ToString()?.Length ?? 0),
                ToolResult tr => tr.Result?.ForLlm()?.Length ?? 0,
                _ => 0
            };
        }
        return chars;
    }
}
