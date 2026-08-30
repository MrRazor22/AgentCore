using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Context;

public class ChatContext : IContext
{
    private readonly List<Message> _chat = new();
    private readonly ILLM? _summarizer;
    private readonly ILogger<ChatContext>? _logger;

    private readonly int _contextWindow;
    private readonly int _reserveTokens;
    private readonly int _limit;
    private int _committedTokens;

    private const double CharsPerToken = 4.0;
    private const double SafetyMargin = 1.15;

    private readonly object _lock = new();

    public ChatContext(int contextWindow = 50000, int? reserveTokens = null, ILLM? summarizer = null, ILogger<ChatContext>? logger = null)
    {
        _contextWindow = contextWindow;
        _reserveTokens = reserveTokens ?? (contextWindow / 10);
        _limit = _contextWindow - _reserveTokens;
        _summarizer = summarizer;
        _logger = logger;
    }

    public Task AddAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default)
    {
        if (messages == null) throw new ArgumentNullException(nameof(messages));

        lock (_lock)
        {
            _chat.AddRange(messages);

            var lastUsage = messages.LastOrDefault(m => m.Metadata?.Usage != null)?.Metadata?.Usage;
            _committedTokens = lastUsage != null
                ? (lastUsage.InputTokens + lastUsage.OutputTokens)
                : _chat.Sum(Estimate);
        }

        _logger?.LogInformation(
            "Messages added to context. AddedCount={AddedCount}, TotalMessages={TotalMessages}, CommittedTokens={CommittedTokens}",
            messages.Count,
            _chat.Count,
            _committedTokens);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(
        CancellationToken ct = default)
    {
        int estimatedTotal;
        lock (_lock) estimatedTotal = _committedTokens; 

        _logger?.LogInformation(
             "Retrieving context messages. Strategy={Strategy}, TotalMessages={TotalMessages}, EstimatedTokens={EstimatedTokens}, Limit={Limit}",
             _summarizer != null ? "Summary" : "Trim",
             _chat.Count,
             estimatedTotal,
             _limit);

        if (estimatedTotal > _limit)
            await CompactChatAsync(ct).ConfigureAwait(false); 

        lock (_lock) return _chat; 
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
                _committedTokens = _chat.Sum(Estimate);
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
                var eventStream = _summarizer.StreamAsync(tempChat, responseSchema: null, tools: null, ct: ct);
                var message = new StreamingMessage(eventStream);
                await foreach (var _ in message.ContentsStream(ct).ConfigureAwait(false)) { }
                var summary = message.Contents.OfType<Text>().FirstOrDefault()?.Value ?? string.Empty;
                return summary.Trim();
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
            var lastMessage = _chat.Count > (systemMessage != null ? 2 : 1) ? _chat[^1] : null;
            _chat.Clear();
            if (systemMessage != null) _chat.Add(systemMessage);
            _chat.Add(new Message(Role.User, new Text($"Context compacted due to overflow. Summary of previous interactions:\n{summary}")));
            if (lastMessage != null && lastMessage.Role != Role.System)
            {
                _chat.Add(lastMessage);
            }
            _committedTokens = _chat.Sum(Estimate);
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
