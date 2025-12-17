using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgentCore.Tests.Tokens
{

    public sealed class TokenManager_Tests
    {
        private readonly TokenManager _manager;

        public TokenManager_Tests()
        {
            var logger = new Mock<ILogger<TokenManager>>();
            _manager = new TokenManager(logger.Object);
        }

        [Fact]
        public void Count_NullOrEmpty_ReturnsZero()
        {
            Assert.Equal(0, _manager.Count(null!));
            Assert.Equal(0, _manager.Count(string.Empty));
        }

        [Fact]
        public void Count_ShortString_ReturnsAtLeastOne()
        {
            Assert.Equal(1, _manager.Count("a"));
            Assert.Equal(1, _manager.Count("abc"));
        }

        [Fact]
        public void Count_LongString_UsesApproximation()
        {
            var payload = new string('a', 400);
            var tokens = _manager.Count(payload);
            Assert.Equal(100, tokens);
        }

        [Fact]
        public void Record_ZeroUsage_DoesNothing()
        {
            _manager.Record(new TokenUsage(0, 0));
            var totals = _manager.GetTotals();

            Assert.Equal(0, totals.InputTokens);
            Assert.Equal(0, totals.OutputTokens);
        }

        [Fact]
        public void Record_AccumulatesCorrectly()
        {
            _manager.Record(new TokenUsage(10, 5));
            _manager.Record(new TokenUsage(3, 2));

            var totals = _manager.GetTotals();
            Assert.Equal(13, totals.InputTokens);
            Assert.Equal(7, totals.OutputTokens);
            Assert.Equal(20, totals.Total);
        }

        [Fact]
        public void ResolveAndRecord_UsesReportedUsage_WhenProvided()
        {
            var reported = new TokenUsage(50, 60);

            var result = _manager.ResolveAndRecord(
                "short",
                "short",
                reported
            );

            Assert.Equal(50, result.InputTokens);
            Assert.Equal(60, result.OutputTokens);

            var totals = _manager.GetTotals();
            Assert.Equal(50, totals.InputTokens);
            Assert.Equal(60, totals.OutputTokens);
        }

        [Fact]
        public void ResolveAndRecord_FallsBackToApprox_WhenUsageNull()
        {
            var result = _manager.ResolveAndRecord(
                "12345678",   // 8 chars -> ~2 tokens
                "1234567890", // 10 chars -> ~2 tokens
                null
            );

            Assert.Equal(2, result.InputTokens);
            Assert.Equal(2, result.OutputTokens);

            var totals = _manager.GetTotals();
            Assert.Equal(2, totals.InputTokens);
            Assert.Equal(2, totals.OutputTokens);
        }

        [Fact]
        public void ResolveAndRecord_EmptyPayloads_ResultZero()
        {
            var result = _manager.ResolveAndRecord("", "", null);

            Assert.Equal(0, result.InputTokens);
            Assert.Equal(0, result.OutputTokens);

            var totals = _manager.GetTotals();
            Assert.Equal(0, totals.Total);
        }

        [Fact]
        public void GetTotals_ReturnsSnapshot_NotReference()
        {
            _manager.Record(new TokenUsage(5, 5));

            var totals1 = _manager.GetTotals();
            totals1.InputTokens = 1000;

            var totals2 = _manager.GetTotals();
            Assert.Equal(5, totals2.InputTokens);
        }
    }

}

