using Microsoft.Extensions.Logging;

namespace AgentCore.Tokens;

public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0)
{
    public int Total => InputTokens + OutputTokens;
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
    private readonly object _lock = new();

    public void Record(TokenUsage usage)
    {
        if (usage.InputTokens <= 0 && usage.OutputTokens <= 0) return;

        lock (_lock)
        {
            _totalInput += usage.InputTokens;
            _totalOutput += usage.OutputTokens;
            _logger.LogDebug("Token usage: In={In} Out={Out} | Cumulative: {Total}",
                usage.InputTokens, usage.OutputTokens, _totalInput + _totalOutput);
        }
    }

    public TokenUsage GetTotals()
    {
        lock (_lock) { return new TokenUsage(_totalInput, _totalOutput); }
    }
}
