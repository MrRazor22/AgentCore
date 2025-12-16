using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using System;

namespace AgentCore.Tokens
{
    public class TokenUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int Total => InputTokens + OutputTokens;

        public TokenUsage(int input = 0, int output = 0)
        {
            InputTokens = input;
            OutputTokens = output;
        }
    }

    public interface ITokenManager
    {
        int Count(string payload);
        void Record(TokenUsage usage);
        TokenUsage ResolveAndRecord(
            string requestPayload,
            string responsePayload,
            TokenUsage? usageReported);
        TokenUsage GetTotals();
    }

    public sealed class TokenManager : ITokenManager
    {
        private readonly ILogger<TokenManager> _logger;

        private TokenUsage _cumulativeTokens = new TokenUsage(0, 0);
        private readonly object _lock = new object();

        public TokenManager(ILogger<TokenManager> logger)
        {
            _logger = logger;
        }

        public int Count(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return 0;

            int approx = payload.Length / 4;
            return approx > 0 ? approx : 1;
        }

        public void Record(TokenUsage usage)
        {
            if (usage.InputTokens <= 0 && usage.OutputTokens <= 0)
                return;

            lock (_lock)
            {
                _cumulativeTokens.InputTokens += usage.InputTokens;
                _cumulativeTokens.OutputTokens += usage.OutputTokens;

                _logger.LogInformation("Token Usage Recorded In: {In} | Out: {Out} | Total so far: {total}",
                    usage.InputTokens,
                    usage.OutputTokens,
                    _cumulativeTokens.Total
                );
            }
        }
        public TokenUsage ResolveAndRecord(
            string requestPayload,
            string responsePayload,
            TokenUsage? usageReported)
        {
            int inTok = Count(requestPayload);
            int outTok = Count(responsePayload);

            _logger.LogDebug(
                "Approximated Token Usage In: {In} | Out: {Out}",
                inTok, outTok
            );

            var finalUsage = usageReported ?? new TokenUsage(inTok, outTok);

            Record(finalUsage);
            return finalUsage;
        }

        public TokenUsage GetTotals()
        {
            lock (_lock)
            {
                return _cumulativeTokens;
            }
        }
    }
}
