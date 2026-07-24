using AgentCore.LLM.Chat;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.LLM;

public interface ITokenCounter
{
    Task<int> EstimateAsync(IEnumerable<Message> messages, CancellationToken ct = default);
    Task<int> EstimateAsync(IEnumerable<Tool> tools, CancellationToken ct = default);
    void ObserveActualCount(IEnumerable<Message> messages, IReadOnlyList<Tool>? tools, int actualInputTokens);
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

    public Task<int> EstimateAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        if (messages == null || !messages.Any())
            return Task.FromResult(0);

        int tokens = EstimateTokens(GetCharCount(messages));

        _logger.LogDebug(
            "Token estimation: MessageCount={Count} EstimatedTokens={Tokens}",
            messages.Count(),
            tokens);

        return Task.FromResult(tokens);
    }

    public Task<int> EstimateAsync(IEnumerable<Tool> tools, CancellationToken ct = default)
    {
        if (tools == null || !tools.Any())
            return Task.FromResult(0);

        int toolChars = 0;
        foreach (var tool in tools)
        {
            toolChars += GetToolCharacterCount(tool);
        }

        int tokens = EstimateTokens(toolChars);
        return Task.FromResult(tokens);
    }

    private static int GetToolCharacterCount(Tool tool)
    {
        return tool.Name.Length + tool.Description.Length + tool.ParametersSchema.ToJsonNode().ToJsonString().Length;
    }

    public void ObserveActualCount(IEnumerable<Message> messages, IReadOnlyList<Tool>? tools, int actualInputTokens)
    {
        if (messages is null || actualInputTokens <= 0)
            return;

        int chars = GetCharCount(messages);

        if (tools is not null)
        {
            foreach (var tool in tools)
            {
                chars += GetToolCharacterCount(tool);
            }
        }

        Calibrate(chars, actualInputTokens);
    }

    private void Calibrate(int charCount, int actualTokens)
    {
        if (charCount <= 0 || actualTokens <= 0) return;

        double currentRatio = (double)charCount / actualTokens;

        // Prevent wild outliers from destroying the ratio
        if (currentRatio < 1.0 || currentRatio > 10.0)
        {
            _logger.LogWarning("Token calibration rejected: CharCount={Char} TokenCount={Tokens} Ratio={Ratio} (out of range 1.0-10.0)",
                charCount, actualTokens, currentRatio);
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