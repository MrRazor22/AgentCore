using AgentCore.Conversation;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tokens;

public interface ITokenCounter
{
    Task<int> CountAsync(IEnumerable<Message> messages, CancellationToken ct = default);
    void RecordActualInput(IEnumerable<Message> messages, IReadOnlyList<Tool>? tools, int actualInputTokens);
}
public sealed class ApproximateTokenCounter : ITokenCounter
{
    private readonly object _lock = new();
    private readonly ILogger<ApproximateTokenCounter> _logger;
    private const double EmaAlpha = 0.1;
    private double _charsPerToken;
    private readonly double _safetyMargin;

    public ApproximateTokenCounter(double initialCharsPerToken = 5.0, double safetyMargin = 1.15, ILogger<ApproximateTokenCounter>? logger = null)
    {
        _charsPerToken = initialCharsPerToken;
        _safetyMargin = safetyMargin;
        _logger = logger ?? NullLogger<ApproximateTokenCounter>.Instance;
    }
     
    public Task<int> CountAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        if (messages == null || !messages.Any())
            return Task.FromResult(0);

        int tokens = EstimateTokens(GetCharCount(messages));

        _logger.LogDebug(
            "Token count: MessageCount={Count} EstimatedTokens={Tokens}",
            messages.Count(),
            tokens);

        return Task.FromResult(tokens);
    }

    public void RecordActualInput(IEnumerable<Message> messages, IReadOnlyList<Tool>? tools, int actualInputTokens)
    {
        if (messages == null || actualInputTokens <= 0) return;

        int toolChars = 0;

        if (tools != null)
            foreach (var tool in tools)
                toolChars += tool.Name.Length + tool.Description.Length + tool.ParametersSchema.ToString().Length;

        int messageTokens = Math.Max(1, actualInputTokens - EstimateTokens(toolChars));

        Calibrate(messages, messageTokens);
    }

    private void Calibrate(IEnumerable<Message> messages, int actualMessageTokens)
    {
        if (messages == null || actualMessageTokens <= 0) return;

        int charCount = GetCharCount(messages);
        if (charCount <= 0) return;

        double currentRatio = (double)charCount / actualMessageTokens;

        // Prevent wild outliers from destroying the ratio
        if (currentRatio < 1.0 || currentRatio > 10.0)
        {
            _logger.LogWarning("Token calibration rejected: CharCount={Char} TokenCount={Tokens} Ratio={Ratio} (out of range 1.0-10.0)",
                charCount, actualMessageTokens, currentRatio);
            return;
        }

        lock (_lock)
        {
            double oldRatio = _charsPerToken;
            _charsPerToken = EmaAlpha * currentRatio + (1 - EmaAlpha) * _charsPerToken;
            _logger.LogDebug("Token calibration: OldRatio={Old} NewRatio={New} (EMA α={Alpha})",
                oldRatio, _charsPerToken, EmaAlpha);
        }
    }

    private int EstimateTokens(int charCount)
    {
        if (charCount <= 0) return 0;

        double charsPerToken;
        lock (_lock) charsPerToken = _charsPerToken;
        return (int)((charCount / charsPerToken) * _safetyMargin);
    }

    private static int GetCharCount(IEnumerable<Message> messages)
    {
        int total = 0;

        foreach (var message in messages)
        {
            int chars = 4; // message overhead

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case Text text:
                        chars += text.Value?.Length ?? 0;
                        break;

                    case Reasoning:
                        break;

                    case ToolCall toolCall:
                        chars += toolCall.Name.Length;
                        chars += toolCall.Arguments?.ToString()?.Length ?? 0;
                        break;

                    case ToolResult toolResult:
                        chars += toolResult.Result?.ForLlm()?.Length ?? 0;
                        break;
                }
            }

            total += chars;
        }

        return total;
    }
}