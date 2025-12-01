using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgentCore.Tests.Tokens
{
    public class FakeLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) => null!;
        public bool IsEnabled(LogLevel l) => false;
        public void Log<TState>(LogLevel l, EventId id, TState s, Exception e, Func<TState, Exception, string> f) { }
    }

    public class TokenManager_Tests
    {
        [Fact]
        public void Count_Empty_ReturnsZero()
        {
            var tm = new TokenManager(new FakeLogger<TokenManager>());
            Assert.Equal(0, tm.Count(""));
            Assert.Equal(0, tm.Count(null));
        }

        [Fact]
        public void Count_UsesLengthDiv4_Min1()
        {
            var tm = new TokenManager(new FakeLogger<TokenManager>());
            Assert.Equal(1, tm.Count("a"));        // 1/4 => 0 => min 1
            Assert.True(tm.Count("abcdabcd") >= 2);
        }

        [Fact]
        public void Record_DoesNothing_WhenZero()
        {
            var tm = new TokenManager(new FakeLogger<TokenManager>());
            tm.Record(new TokenUsage(0, 0));
            Assert.Equal(0, tm.GetTotals().Total);
        }

        [Fact]
        public void Record_Accumulates()
        {
            var tm = new TokenManager(new FakeLogger<TokenManager>());
            tm.Record(new TokenUsage(10, 5));
            tm.Record(new TokenUsage(2, 3));
            Assert.Equal(20, tm.GetTotals().Total);
        }
    }
}

