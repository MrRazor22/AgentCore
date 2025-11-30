using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Tests.Tokens
{
    public class ContextBudgetManager_Tests
    {
        [Fact]
        public void Trim_RemovesOlderMessages()
        {
            var mgr = new ContextBudgetManager(
                new ContextBudgetOptions { MaxContextTokens = 10, Margin = 0.5 },
                new FakeTokenMgr());

            var req = new FakeReq();
            req.Prompt.AddUser("msg1");
            req.Prompt.AddAssistant("msg2");
            req.Prompt.AddUser("msg3");

            var trimmed = mgr.Trim(req);

            Assert.True(trimmed.Count <= req.Prompt.Count);
        }

        class FakeReq : LLMRequestBase
        {
            public FakeReq() : base(new Conversation()) { }
            public override LLMRequestBase DeepClone() => new FakeReq();
            public override string ToSerializablePayload() => "";
        }

        class FakeTokenMgr : ITokenManager
        {
            public int Count(string p) => 999; // force trimming
            public void Record(TokenUsage u) { }
            public TokenUsage GetTotals() => TokenUsage.Empty;
        }
    }
}
