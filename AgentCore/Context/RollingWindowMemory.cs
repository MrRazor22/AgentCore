using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;

namespace AgentCore.Memory;

public sealed class RollingWindowMemoryOptions
{
    public int WindowSize { get; set; } = 10;
    public bool EnableSummarization { get; set; } = true;
    public int SummaryThreshold { get; set; } = 20;
    public string SummaryLabel { get; set; } = "conversation_summary";
    public string RecentLabel { get; set; } = "recent_context";
}

public sealed class RollingWindowMemory : IAgentMemory
{
    private readonly ILLMProvider _llm;
    private readonly ITokenCounter _tokenCounter;
    private readonly RollingWindowMemoryOptions _options;
    private readonly ILogger<RollingWindowMemory> _logger;
    private readonly object _gate = new();

    private readonly List<Message> _history = [];
    private string _summary = "";

    public RollingWindowMemory(
        ILLMProvider llm,
        ITokenCounter tokenCounter,
        RollingWindowMemoryOptions? options = null,
        ILogger<RollingWindowMemory>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _options = options ?? new RollingWindowMemoryOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RollingWindowMemory>.Instance;
    }

    public Task<IReadOnlyList<Message>> RecallAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        var result = new List<Message>();

        if (_options.EnableSummarization && !string.IsNullOrEmpty(_summary))
        {
            var content = $"<{_options.SummaryLabel}>\n{_summary}\n</{_options.SummaryLabel}>";
            result.Add(new Message(Role.System, new Text(content)));
        }

        if (messages.Count > 0)
        {
            var recent = messages.TakeLast(_options.WindowSize).ToList();
            var recentText = string.Join("\n", recent.Select(MessageToString));
            var content = $"<{_options.RecentLabel}>\n{recentText}\n</{_options.RecentLabel}>";
            result.Add(new Message(Role.System, new Text(content)));
        }

        return Task.FromResult<IReadOnlyList<Message>>(result);
    }

    public async Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        List<Message> toSummarize;
        lock (_gate)
        {
            _history.AddRange(messages);
            if (_history.Count <= _options.WindowSize || !_options.EnableSummarization)
                return;
            toSummarize = _history.Take(_history.Count - _options.WindowSize).ToList();
            if (toSummarize.Count < _options.SummaryThreshold)
                return;
        }

        var newSummary = await SummarizeAsync(toSummarize, ct).ConfigureAwait(false);

        lock (_gate)
        {
            _summary = newSummary;
            if (_history.Count > _options.WindowSize)
                _history.RemoveRange(0, _history.Count - _options.WindowSize);
        }
    }

    private static string MessageToString(Message m)
    {
        var content = string.Join("", m.Contents.Select(c => c.ForLlm()));
        return $"<{m.Role}>: {content}";
    }

    private async Task<string> SummarizeAsync(List<Message> messages, CancellationToken ct)
    {
        var prompt = $"""
        Summarize this conversation concisely, preserving key information, decisions, and facts:

        {string.Join("\n", messages.Select(MessageToString))}

        Summary:
        """;

        try
        {
            var systemMsg = new Message(Role.System, new Text("You are a helpful assistant that summarizes conversations concisely."));
            var userMsg = new Message(Role.User, new Text(prompt));
            var options = new LLMOptions { Temperature = 0.3f, ToolCallMode = ToolCallMode.None };

            var sb = new System.Text.StringBuilder();
            await foreach (var delta in _llm.StreamAsync([systemMsg, userMsg], options, null, ct).ConfigureAwait(false))
            {
                if (delta is TextDelta td) sb.Append(td.Value);
            }
            var summary = sb.ToString().Trim();
            _logger.LogDebug("Conversation summarized: OriginalMessages={Count} SummaryLength={Len}", messages.Count, summary.Length);
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to summarize conversation, using truncation fallback");
            return TruncateToSummary(messages);
        }
    }

    private string TruncateToSummary(List<Message> messages)
    {
        var recentMessages = messages.TakeLast(5).ToList();
        return $"[Old conversation truncated. Last {recentMessages.Count} messages: {string.Join(" | ", recentMessages.Select(m => $"<{m.Role}>: {(m.Contents.FirstOrDefault() as Text)?.Value ?? "..."}"))}]";
    }
}