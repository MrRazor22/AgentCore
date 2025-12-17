using AgentCore.Chat;
using AgentCore.Tokens;
using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Newtonsoft.Json.Linq;

namespace AgentCore.Tests.Tokens
{
    public sealed class ContextManager_Tests
    {
        private readonly ContextManager _manager;
        private readonly FakeTokenManager _tokens;

        public ContextManager_Tests()
        {
            _tokens = new FakeTokenManager();
            var logger = new Mock<ILogger<ContextManager>>();

            _manager = new ContextManager(
                new ContextBudgetOptions
                {
                    MaxContextTokens = 100,
                    Margin = 0.5,
                    KeepLastMessages = 2
                },
                _tokens,
                logger.Object
            );
        }

        [Fact]
        public void Trim_NullConversation_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.Trim(null!));
        }

        [Fact]
        public void Trim_WithinBudget_ReturnsClone()
        {
            var conv = ConversationWithTurns(2);
            var trimmed = _manager.Trim(conv);

            Assert.NotSame(conv, trimmed);
            Assert.Equal(conv.Count, trimmed.Count);
        }

        [Fact]
        public void Trim_Preserves_LastUserAssistantPair()
        {
            _tokens.ForceHighCount = true;

            var conv = ConversationWithTurns(5);
            var trimmed = _manager.Trim(conv);

            var ua = trimmed
                .Where(m => m.Role == Role.User || m.Role == Role.Assistant)
                .ToList();

            Assert.True(ua.Count >= 2);
            Assert.Equal(Role.User, ua[^2].Role);
            Assert.Equal(Role.Assistant, ua[^1].Role);
        }

        [Fact]
        public void Trim_Preserves_UserOnly_WhenNoAssistantYet()
        {
            _tokens.ForceHighCount = true;

            var c = new Conversation();
            c.Add(Role.User, new TextContent("u1"));
            c.Add(Role.User, new TextContent("u2"));

            var trimmed = _manager.Trim(c);

            Assert.Equal(Role.User, trimmed.Last().Role);
        }

        [Fact]
        public void Trim_SystemMessages_AlwaysPreserved()
        {
            var c = ConversationWithTurns(3);
            c.Add(Role.System, new TextContent("sys"));

            var trimmed = _manager.Trim(c);

            Assert.Contains(trimmed, m => m.Role == Role.System);
        }

        [Fact]
        public void Trim_Preserves_LastToolCall_AndToolResult()
        {
            var c = ConversationWithTurns(3);
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
            var c = ConversationWithTurns(5);

            var trimmed = _manager.Trim(c, requiredGap: 40);

            var count = _tokens.Count(trimmed.ToJson());
            Assert.True(count <= 60);
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

            // MaxContextTokens = 100, Margin = 0.5 → max gap = 50
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _manager.Trim(c, requiredGap: 60));
        }

        [Fact]
        public void Trim_Never_Returns_EmptyConversation()
        {
            _tokens.ForceHighCount = true;

            var c = ConversationWithTurns(1);
            var trimmed = _manager.Trim(c);

            Assert.NotEmpty(trimmed);
        }

        // ---------------- helpers ----------------

        private static Conversation ConversationWithTurns(int turns)
        {
            var c = new Conversation();
            for (int i = 0; i < turns; i++)
            {
                c.Add(Role.User, new TextContent("u" + i));
                c.Add(Role.Assistant, new TextContent("a" + i));
            }
            return c;
        }

        private sealed class FakeTokenManager : ITokenManager
        {
            public bool ForceHighCount;

            public int Count(string payload)
                => ForceHighCount ? 1000 : payload.Length;

            public void Record(TokenUsage usage) { }

            public TokenUsage ResolveAndRecord(string r, string s, TokenUsage? u)
                => u ?? new TokenUsage();

            public TokenUsage GetTotals()
                => new TokenUsage();
        }
    }
}
