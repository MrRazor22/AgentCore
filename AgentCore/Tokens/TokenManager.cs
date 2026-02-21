using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentCore.Tokens;

public class TokenUsage(int input = 0, int output = 0)
{
    public int InputTokens { get; set; } = input;
    public int OutputTokens { get; set; } = output;
    public int Total => InputTokens + OutputTokens;
}

public interface ITokenManager
{
    int AppromimateCount(string payload);
    void Record(TokenUsage usage);
    TokenUsage ResolveAndRecord(string requestPayload, string responsePayload, TokenUsage? usageReported);
    TokenUsage GetTotals();
}

public sealed class TokenManager(ITokenCounter _counter, ILogger<TokenManager> _logger) : ITokenManager
{
    private TokenUsage _cumulativeTokens = new();
    private readonly object _lock = new();

    public int AppromimateCount(string payload) => _counter.Count(payload);

    public void Record(TokenUsage usage)
    {
        if (usage.InputTokens <= 0 && usage.OutputTokens <= 0) return;

        lock (_lock)
        {
            _cumulativeTokens.InputTokens += usage.InputTokens;
            _cumulativeTokens.OutputTokens += usage.OutputTokens;
            _logger.LogDebug("Token Usage Recorded In: {In} | Out: {Out} | Total so far: {total}",
                usage.InputTokens, usage.OutputTokens, _cumulativeTokens.Total);
        }
    }

    public TokenUsage ResolveAndRecord(string requestPayload, string responsePayload, TokenUsage? usageReported)
    {
        int inTok = AppromimateCount(requestPayload);
        int outTok = AppromimateCount(responsePayload);

        _logger.LogTrace("Approximated Token Usage In: {In} | Out: {Out}", inTok, outTok);

        if (usageReported != null)
            _logger.LogTrace("Token Approx Accuracy In={InAcc:F0}% Out={OutAcc:F0}%",
                usageReported.InputTokens <= 0 ? 100 : 100 * (1 - Math.Abs(inTok - usageReported.InputTokens) / (double)usageReported.InputTokens),
                usageReported.OutputTokens <= 0 ? 100 : 100 * (1 - Math.Abs(outTok - usageReported.OutputTokens) / (double)usageReported.OutputTokens));

        var finalUsage = usageReported ?? new TokenUsage(inTok, outTok);
        Record(finalUsage);
        return finalUsage;
    }

    public TokenUsage GetTotals()
    {
        lock (_lock) { return new TokenUsage(_cumulativeTokens.InputTokens, _cumulativeTokens.OutputTokens); }
    }
}
