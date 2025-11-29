using AgentCore.LLM.Client;
using Microsoft.Extensions.Logging;
using System;

namespace AgentCore.Tokens
{
    public class TokenUsage
    {
        public int InputTokens { get; }
        public int OutputTokens { get; }
        public int Total => InputTokens + OutputTokens;

        public TokenUsage(int input, int output)
        {
            InputTokens = input;
            OutputTokens = output;
        }

        public static readonly TokenUsage Empty = new TokenUsage(0, 0);
    }

    public interface ITokenManager
    {
        int Count(string payload);
        void Record(TokenUsage usage);
        TokenUsage GetTotals();
    }

    public sealed class TokenManager : ITokenManager
    {
        private readonly ILogger<TokenManager> _logger;

        private int _totalIn;
        private int _totalOut;
        private readonly object _lock = new object();

        public TokenManager(ILogger<TokenManager> logger)
        {
            _logger = logger;
        }

        // simple estimation
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
                _totalIn += usage.InputTokens;
                _totalOut += usage.OutputTokens;

                _logger.LogInformation(
                    "TokenManager +{In} In, +{Out} Out",
                    usage.InputTokens,
                    usage.OutputTokens
                );
            }
        }

        public TokenUsage GetTotals()
        {
            lock (_lock)
            {
                return new TokenUsage(_totalIn, _totalOut);
            }
        }
    }
}
