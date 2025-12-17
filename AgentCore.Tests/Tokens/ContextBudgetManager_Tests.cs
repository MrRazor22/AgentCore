using AgentCore.Chat;
using AgentCore.Tokens;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Xunit;

namespace AgentCore.Tests.Tokens
{
    public sealed class FakeTokenMgr : ITokenManager
    {
        public int NextCount = 0;
        public int Count(string payload) => NextCount;
        public void Record(TokenUsage usage) { }
        public TokenUsage GetTotals() => new TokenUsage();
    }

    public class ContextBudgetManager_Tests
    {
        Conversation MakeConvo()
        {
            var c = new Conversation();
            c.Add(Role.System, "sys");
            c.Add(Role.User, "u1");
            c.Add(Role.Assistant, "a1");
            c.Add(Role.User, "u2");
            c.Add(Role.Assistant, "a2");
            return c;
        }

        [Fact]
        public void Trim_Throws_OnNull()
        {
            var mgr = new ContextManager(new ContextBudgetOptions(), new FakeTokenMgr(), null);
            Assert.Throws<ArgumentNullException>(() => mgr.Trim(null));
        }

        [Fact]
        public void Trim_NoChange_WhenWithinLimit()
        {
            var tm = new FakeTokenMgr { NextCount = 5 };
            var mgr = new ContextManager(new ContextBudgetOptions { MaxContextTokens = 100, Margin = 1.0 }, tm, null);

            var c = MakeConvo();
            var trimmed = mgr.Trim(c);

            Assert.Equal(c.Count, trimmed.Count);
        }

        [Fact]
        public void Trim_DropsTools_BeforeDroppingHistory()
        {
            var tm = new FakeTokenMgr();
            var opts = new ContextBudgetOptions { MaxContextTokens = 100, Margin = 0.2 };
            var mgr = new ContextManager(opts, tm, null);

            var c = new Conversation();
            c.Add(Role.System, "sys");
            c.Add(Role.User, "u1");
            c.Add(Role.Tool, new ToolCallResult(new ToolCall("id", "x", new JObject()), "res"));
            c.Add(Role.Assistant, "a1");

            tm.NextCount = 1000; // cause overflow

            var trimmed = mgr.Trim(c);

            Assert.DoesNotContain(trimmed, x => x.Role == Role.Tool);
        }

        [Fact]
        public void Trim_RemovesOldUserAssistantMessages_SlidingWindow()
        {
            var tm = new FakeTokenMgr();
            tm.NextCount = 1000;

            var mgr = new ContextManager(
                new ContextBudgetOptions { MaxContextTokens = 100, Margin = 0.2 },
                tm,
                null
            );

            var c = MakeConvo();
            var trimmed = mgr.Trim(c);

            Assert.True(trimmed.Any(x => x.Role == Role.System));       // system kept
            Assert.False(trimmed.Any(chat => chat.Content is TextContent tc && tc.Text == "u1"));
            Assert.False(trimmed.Any(chat => chat.Content is TextContent tc && tc.Text == "a1"));
        }

        [Fact]
        public void Trim_RespectsRequiredGap()
        {
            var tm = new FakeTokenMgr();
            tm.NextCount = 1000;

            var mgr = new ContextManager(
                new ContextBudgetOptions { MaxContextTokens = 1000 },
                tm,
                null
            );

            var convo = MakeConvo();
            var trimmed = mgr.Trim(convo, requiredGap: 500);

            Assert.True(trimmed.Count < convo.Count);
        }
    }
}
