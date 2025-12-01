using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.LLM.Pipeline;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore.Tests.LLM
{
    public sealed class LLMPipeline_Tests
    {
        [Fact]
        public async Task Pipeline_PassesChunks_AndBuildsResponse()
        {
            var ctx = new FakeCtx();
            var tokens = new FakeTokenManager();
            var retry = new FakeRetry();
            var pipeline = new LLMPipeline(ctx, tokens, retry, NullLogger.Instance);

            var handler = new FakeHandler();
            var req = new LLMRequest(new Conversation().AddUser("x"));

            LLMResponseBase resp = await pipeline.RunAsync(
                req,
                handler,
                _ => FakeStream(),
                null,
                CancellationToken.None
            );

            Assert.IsType<LLMResponse>(resp);
            Assert.Equal("stop", resp.FinishReason);
            Assert.True(resp.TokenUsage.Total > 0);
        }

        private async IAsyncEnumerable<LLMStreamChunk> FakeStream()
        {
            yield return new LLMStreamChunk(StreamKind.Text, "hi");
            yield return new LLMStreamChunk(StreamKind.Usage, new TokenUsage(3, 2));
            yield return new LLMStreamChunk(StreamKind.Finish, null, "stop");
            await Task.CompletedTask;
        }

        private sealed class FakeHandler : IChunkHandler
        {
            private readonly StringBuilder sb = new StringBuilder();
            public void PrepareRequest(LLMRequestBase req) { }
            public void OnChunk(LLMStreamChunk c)
            {
                if (c.Kind == StreamKind.Text)
                    sb.Append(c.AsText());
            }
            public LLMResponseBase BuildResponse(string f, TokenUsage u)
                => new LLMResponse(sb.ToString(), null, f, u);
        }

        private sealed class FakeCtx : IContextBudgetManager
        {
            public Conversation Trim(Conversation c, int? x = null) => c;
        }

        private sealed class FakeRetry : IRetryPolicy
        {
            public async IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
                LLMRequestBase r,
                Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> f,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await foreach (var c in f(r))
                    yield return c;
            }
        }

        private sealed class FakeTokenManager : ITokenManager
        {
            public int Count(string payload) => 1;
            public void Record(TokenUsage u) { }
            public TokenUsage GetTotals() => new TokenUsage(0, 0);
        }
    }
}
