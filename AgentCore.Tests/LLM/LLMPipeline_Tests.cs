using AgentCore.LLM.Client;
using AgentCore.LLM.Pipeline;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Tests.LLM
{
    public class LLMPipeline_Tests
    {
        [Fact]
        public async Task Pipeline_ProcessesChunks()
        {
            var pipeline = new LLMPipeline(
                new FakeCtxMgr(),
                new FakeTokenMgr(),
                new FakeRetry(),
                NullLogger.Instance);

            var handler = new FakeHandler();
            var req = new FakeReq();

            var res = await pipeline.RunAsync(
                req,
                handler,
                _ => FakeStream(),
                null,
                CancellationToken.None);

            Assert.Equal("stop", res.FinishReason);
            Assert.Contains("hi", handler.Text);
        }

        async IAsyncEnumerable<LLMStreamChunk> FakeStream()
        {
            yield return new LLMStreamChunk(StreamKind.Text, "hi");
            yield return new LLMStreamChunk(StreamKind.Finish, finish: "stop");
        }

        class FakeReq : LLMRequestBase
        {
            public FakeReq() : base(new AgentCore.Chat.Conversation()) { }
            public override LLMRequestBase DeepClone() => new FakeReq();
            public override string ToSerializablePayload() => "";
        }

        class FakeCtxMgr : IContextBudgetManager
        {
            public AgentCore.Chat.Conversation Trim(LLMRequestBase r, int? g) => r.Prompt;
        }

        class FakeRetry : IRetryPolicy
        {
            public async IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(LLMRequestBase o, System.Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> f, CancellationToken ct = default)
            {
                await foreach (var c in f(o)) yield return c;
            }
        }

        class FakeTokenMgr : ITokenManager
        {
            public int Count(string p) => 1;
            public void Record(TokenUsage u) { }
            public TokenUsage GetTotals() => TokenUsage.Empty;
        }

        class FakeHandler : IChunkHandler
        {
            public string Text = "";
            public void PrepareRequest(LLMRequestBase r) { }
            public void OnChunk(LLMStreamChunk c)
            {
                if (c.AsText() != null) Text += c.AsText();
            }
            public LLMResponseBase BuildResponse(string f, TokenUsage t)
                => new LLMResponse(Text, null, f, t);
        }
    }
}
