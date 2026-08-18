using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Context;

public class ChatContext : IContext
{
    private readonly List<Message> _chat = new();
    private readonly List<Message> _staged = new();
    private readonly ILLM? _summarizer;
    private readonly ILogger<ChatContext>? _logger;

    private readonly int _contextWindow;
    private readonly int _reserveTokens;
    private readonly int _limit;

    private const double CharsPerToken = 4.0;
    private const double SafetyMargin = 1.15;

    private readonly object _lock = new();

    private TokenUsage TokenUsage { get; set; } = new(0, 0);

    public ChatContext(int contextWindow = 50000, int? reserveTokens = null, ILLM? summarizer = null, ILogger<ChatContext>? logger = null)
    {
        _contextWindow = contextWindow;
        _reserveTokens = reserveTokens ?? (contextWindow / 10);
        _limit = _contextWindow - _reserveTokens;
        _summarizer = summarizer;
        _logger = logger;
    }

    public Task StageAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            _staged.AddRange(messages);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Message>> PreparePromptAsync(
        CancellationToken ct = default)
    {
        int stagedEstimate;
        int currentUsage;
        lock (_lock)
        { 
            currentUsage = TokenUsage.InputTokens + TokenUsage.OutputTokens;
            stagedEstimate = _staged.Sum(m => Estimate(m));
        }

        int estimatedTotal = currentUsage + stagedEstimate;

        _logger?.LogInformation(
             "Preparing prompt. Strategy={Strategy}, StagedMessages={StagedMessages}, StagedTokens={StagedTokens}, EstimatedTokens={EstimatedTokens}, Limit={Limit}",
             _summarizer != null ? "Summary" : "Trim",
             _staged.Count,
             stagedEstimate,
             estimatedTotal,
             _limit);

        if (estimatedTotal > _limit)
        {
            _logger?.LogInformation(
                "Compacting conversation context. Strategy={Strategy}, EstimatedTokens={EstimatedTokens}, Limit={Limit}",
                _summarizer != null ? "Summary" : "Trim",
                estimatedTotal,
                _limit);
            await CompactChatAsync(ct).ConfigureAwait(false);
        }

        lock (_lock)
        {
            var preparedPrompt = new List<Message>(_chat);
            preparedPrompt.AddRange(_staged);
            return preparedPrompt;
        }
    }

    public Task CommitAsync(
        IReadOnlyList<Message> response,
        TokenUsage? usage = null,
        CancellationToken ct = default)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        lock (_lock)
        {
            _chat.AddRange(_staged);
            _chat.AddRange(response);

            if (usage != null)
            {
                TokenUsage = usage;
            }
            else
            {
                TokenUsage = new TokenUsage(
                    InputTokens: TokenUsage.InputTokens + _staged.Sum(Estimate),
                    OutputTokens: TokenUsage.OutputTokens + response.Sum(Estimate)
                );
            }

            _staged.Clear();
        }
        _logger?.LogInformation(
            "Conversation updated. TotalMessages={TotalMessages}, InputTokens={InputTokens}, OutputTokens={OutputTokens}",
            _chat.Count,
            TokenUsage.InputTokens,
            TokenUsage.OutputTokens);

        return Task.CompletedTask;
    }

    private async Task CompactChatAsync(CancellationToken ct = default)
    {
        if (_summarizer != null)
        {
            string summary = await GenerateChatSummary(ct).ConfigureAwait(false);
            ReplaceChatWithSummary(summary);
        }
        else
        {
            // Fallback: Rolling window trimming
            lock (_lock)
            {
                // Start checking from the first message after the system prompt (if one exists)
                bool hasSystem = _chat.Any(m => m.Role == Role.System);
                int startIndex = hasSystem ? 1 : 0;
                while (_chat.Count > (startIndex + 1))
                {
                    _chat.RemoveAt(startIndex);
                }
                TokenUsage = new TokenUsage(_chat.Sum(Estimate), 0);
            }
        }
    }

    private async Task<string> GenerateChatSummary(CancellationToken ct)
    {
        if (_summarizer == null) return string.Empty;

        List<Message> tempChat;
        lock (_lock)
        {
            tempChat = _chat.ToList();
        }

        tempChat.Add(new Message(Role.User, new Text("Please summarize our conversation so far, focusing on key details, facts, preferences, and decisions. Keep it concise.")));

        while (true)
        {
            try
            {
                var sb = new StringBuilder();
                await foreach (var evt in _summarizer.StreamAsync(tempChat, responseSchema: null, tools: null, ct: ct).ConfigureAwait(false))
                {
                    if (evt is TextDelta t)
                    {
                        sb.Append(t.Value);
                    }
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                int oldestIndex = tempChat.Count > 0 && tempChat[0].Role == Role.System ? 1 : 0;
                if (tempChat.Count > oldestIndex + 1)
                {
                    var removedMessage = tempChat[oldestIndex];
                    tempChat.RemoveAt(oldestIndex);

                    _logger?.LogWarning(
                        ex,
                        "Chat summary generation failed. Retrying with reduced message history. RemovedMessageRole={Role}, RemovedChars={Length}",
                        removedMessage.Role,
                        removedMessage.Contents.FirstOrDefault()?.ForLlm()?.Length ?? 0);
                }
                else
                {
                    throw;
                }
            }
        }
    }

    private void ReplaceChatWithSummary(string summary)
    {
        lock (_lock)
        {
            var systemMessage = _chat.FirstOrDefault(m => m.Role == Role.System);
            _chat.Clear();
            if (systemMessage != null) _chat.Add(systemMessage);
            _chat.Add(new Message(Role.User, new CompactedSummary(summary)));

            TokenUsage = new TokenUsage(_chat.Sum(Estimate), 0);
        }
    }

    private int Estimate(Message message)
    {
        int chars = 4; // overhead
        foreach (var content in message.Contents)
        {
            chars += content.ForLlm()?.Length ?? 0;
        }
        return (int)((chars / CharsPerToken) * SafetyMargin);
    }
}
