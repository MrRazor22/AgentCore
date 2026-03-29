using Microsoft.Extensions.Logging;

namespace AgentCore.Tokens;

public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0, int ReasoningTokens = 0)
{
    public int Total => InputTokens + OutputTokens + ReasoningTokens;
    public static TokenUsage Empty => new(0, 0, 0);
    public bool IsEmpty => InputTokens == 0 && OutputTokens == 0 && ReasoningTokens == 0;
}

public interface ITokenManager
{
    void Record(TokenUsage usage);
    TokenUsage GetTotals();
}

public sealed class TokenManager(ILogger<TokenManager> _logger) : ITokenManager
{
    private int _totalInput;
    private int _totalOutput;
    private int _totalReasoning;
    private readonly object _lock = new();

    public void Record(TokenUsage usage)
    {
        if (usage.InputTokens <= 0 && usage.OutputTokens <= 0 && usage.ReasoningTokens <= 0) return;

        lock (_lock)
        {
            _totalInput += usage.InputTokens;
            _totalOutput += usage.OutputTokens;
            _totalReasoning += usage.ReasoningTokens;
            _logger.LogDebug("Token usage: In={In} Out={Out} Reason={Reason} | Cumulative: {Total}",
                usage.InputTokens, usage.OutputTokens, usage.ReasoningTokens, _totalInput + _totalOutput + _totalReasoning);
        }
    }

    public TokenUsage GetTotals()
    {
        lock (_lock) { return new TokenUsage(_totalInput, _totalOutput, _totalReasoning); }
    }
}
