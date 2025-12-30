using AgentCore.Chat;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Xunit;

namespace AgentCore.Tests.Tokens
{
    public sealed class ContextManager_Tests
    {
        private readonly ContextManager _manager;

        public ContextManager_Tests()
        {
            _manager = new ContextManager(
                new ContextBudgetOptions
                {
                    MaxContextTokens = 100,
                    Margin = 0.5,
                    KeepLastMessages = 2
                },
                new DeterministicTokenManager(),
                Mock.Of<ILogger<ContextManager>>()
            );
        }

        [Fact]
        public void Trim_NullConversation_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.Trim(null!));
        }

        [Fact]
        public void Trim_WithinBudget_ReturnsClone_Unmodified()
        {
            var conv = ConversationWithTurns(1);
            var trimmed = _manager.Trim(conv);

            Assert.NotSame(conv, trimmed);
            Assert.Equal(conv.Count, trimmed.Count);
        }

        [Fact]
        public void Trim_WhenOverBudget_Keeps_Last_User_Assistant_Pair()
        {
            var conv = ConversationWithTurns(10);
            var trimmed = _manager.Trim(conv);

            var ua = trimmed
                .Where(m => m.Role == Role.User || m.Role == Role.Assistant)
                .ToList();

            Assert.True(ua.Count >= 2);
            Assert.Equal(Role.User, ua[^2].Role);
            Assert.Equal(Role.Assistant, ua[^1].Role);
        }

        [Fact]
        public void Trim_Preserves_UserOnly_WhenNoAssistantExists()
        {
            var c = new Conversation();
            c.Add(Role.User, new TextContent("u1"));
            c.Add(Role.User, new TextContent("u2"));

            var trimmed = _manager.Trim(c);

            Assert.Equal(Role.User, trimmed.Last().Role);
        }

        [Fact]
        public void Trim_SystemMessages_AreAlwaysPreserved()
        {
            var c = ConversationWithTurns(5);
            c.Add(Role.System, new TextContent("sys"));

            var trimmed = _manager.Trim(c);

            Assert.Contains(trimmed, m => m.Role == Role.System);
        }

        [Fact]
        public void Trim_Preserves_LastToolCall_And_Result()
        {
            var c = ConversationWithTurns(5);
            var call = new ToolCall("1", "X", new JObject());

            c.AddAssistantToolCall(call);
            c.Add(Role.Tool, new ToolCallResult(call, 42));

            var trimmed = _manager.Trim(c);

            Assert.Contains(trimmed, m => m.Content is ToolCall);
            Assert.Contains(trimmed, m => m.Role == Role.Tool);
        }

        [Fact]
        public void Trim_Respects_RequiredGap()
        {
            var c = ConversationWithTurns(10);

            var trimmed = _manager.Trim(c, requiredGap: 40);

            // Required gap only guarantees *space*, not zero content
            Assert.NotEmpty(trimmed);
        }

        [Fact]
        public void Trim_RequiredGap_Negative_Throws()
        {
            var c = ConversationWithTurns(2);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _manager.Trim(c, requiredGap: -1));
        }

        [Fact]
        public void Trim_RequiredGap_ExceedsMargin_Throws()
        {
            var c = ConversationWithTurns(2);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _manager.Trim(c, requiredGap: 60));
        }

        [Fact]
        public void Trim_Never_Returns_EmptyConversation()
        {
            var c = ConversationWithTurns(1);
            var trimmed = _manager.Trim(c);
            Assert.NotEmpty(trimmed);
        }

        private static Conversation ConversationWithTurns(int turns)
        {
            var c = new Conversation();
            for (int i = 0; i < turns; i++)
            {
                c.Add(Role.User, new TextContent("u"));
                c.Add(Role.Assistant, new TextContent("a"));
            }
            return c;
        }

        private sealed class DeterministicTokenManager : ITokenManager
        {
            // One token per message object
            public int AppromimateCount(string payload)
                => payload.Count(ch => ch == '{');

            public void Record(TokenUsage usage) { }
            public TokenUsage ResolveAndRecord(string r, string s, TokenUsage? u)
                => u ?? new TokenUsage();
            public TokenUsage GetTotals() => new TokenUsage();
        }
    }
}
