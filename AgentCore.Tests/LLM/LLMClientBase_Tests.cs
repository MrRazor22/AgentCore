using AgentCore.Chat;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Runtime.CompilerServices;
using Xunit;

namespace AgentCore.Tests.LLM
{
    public sealed class LLMClientBase_Tests
    {
        private readonly Mock<IContextManager> _ctx;
        private readonly Mock<ITokenManager> _tokens;
        private readonly RetryPolicy _retry;
        private readonly Mock<IChunkHandler> _handler;
        private readonly ToolRegistryCatalog _tools;
        private readonly ToolCallParser _parser;
        private readonly TestClient _client;

        // ---------- STABLE TOOL ----------
        private static class TestTools
        {
            public static int AddOne(int x) => x + 1;
        }

        public LLMClientBase_Tests()
        {
            _ctx = new Mock<IContextManager>();
            _tokens = new Mock<ITokenManager>();
            _handler = new Mock<IChunkHandler>();

            _ctx.Setup(x => x.Trim(It.IsAny<Conversation>(), It.IsAny<int?>()))
                .Returns<Conversation, int?>((c, _) => c);

            _tokens.Setup(x => x.ResolveAndRecord(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TokenUsage>()))
                .Returns(new TokenUsage());

            _retry = new RetryPolicy(
                Mock.Of<ILogger<RetryPolicy>>(),
                Options.Create(new RetryPolicyOptions { MaxRetries = 0 })
            );

            _tools = new ToolRegistryCatalog();
            _tools.Register((Func<int, int>)TestTools.AddOne);

            _parser = new ToolCallParser(_tools);

            _client = new TestClient(
                new LLMInitOptions(),
                _ctx.Object,
                _tokens.Object,
                _retry,
                _parser,
                _tools,
                _ => _handler.Object,
                NullLogger<LLMExecutor>.Instance
            );
        }

        [Fact]
        public async Task Single_ToolCall_Is_Detected()
        {
            SetupStream(
                ToolDelta("TestTools.AddOne", @"{""x"":1}"),
                Finish(FinishReason.ToolCall)
            );

            var resp = await _client.ExecuteAsync<LLMResponse>(
                new LLMRequest(new Conversation()));

            Assert.NotNull(resp.ToolCall);
            Assert.Equal("TestTools.AddOne", resp.ToolCall!.Name);
        }

        [Fact]
        public async Task Second_ToolCall_Is_Ignored_Not_Thrown()
        {
            SetupStream(
                ToolDelta("TestTools.AddOne", @"{""x"":1}"),
                ToolDelta("TestTools.AddOne", @"{""x"":2}")
            );

            var resp = await _client.ExecuteAsync<LLMResponse>(
                new LLMRequest(new Conversation()));

            Assert.NotNull(resp.ToolCall);
            Assert.Equal("TestTools.AddOne", resp.ToolCall!.Name);
        }

        [Fact]
        public async Task Invalid_Tool_Throws_RetryException()
        {
            SetupStream(
                ToolDelta("NoSuch.Tool", @"{}"),
                Finish(FinishReason.ToolCall)
            );

            await Assert.ThrowsAsync<RetryException>(() =>
                _client.ExecuteAsync<LLMResponse>(
                    new LLMRequest(new Conversation())));
        }

        [Fact]
        public async Task Cancellation_Returns_Cancelled_Response()
        {
            SetupStream();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var resp = await _client.ExecuteAsync<LLMResponse>(
                new LLMRequest(new Conversation()),
                cts.Token);

            Assert.Equal(FinishReason.Cancelled, resp.FinishReason);
        }

        [Fact]
        public async Task Handler_Exception_OnChunk_Bubbles()
        {
            _handler.Setup(h => h.OnChunk(It.IsAny<LLMStreamChunk>()))
                .Throws(new InvalidOperationException());

            SetupStream(new LLMStreamChunk(StreamKind.Text, "hi"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _client.ExecuteAsync<LLMResponse>(
                    new LLMRequest(new Conversation())));
        }

        // ---------- helpers ----------

        private void SetupStream(params LLMStreamChunk[] chunks)
            => _client.SetStream(chunks);

        private static LLMStreamChunk ToolDelta(string name, string json)
            => new LLMStreamChunk(
                StreamKind.ToolCallDelta,
                new ToolCallDelta { Name = name, Delta = json });

        private static LLMStreamChunk Finish(FinishReason r)
            => new LLMStreamChunk(StreamKind.Finish, r);

        private sealed class TestClient : LLMExecutor
        {
            private LLMStreamChunk[] _chunks = Array.Empty<LLMStreamChunk>();

            public TestClient(
                LLMInitOptions opts,
                IContextManager ctx,
                ITokenManager tokens,
                IRetryPolicy retry,
                IToolCallParser parser,
                IToolCatalog tools,
                HandlerResolver resolver,
                ILogger<LLMExecutor> logger)
                : base(opts, ctx, tokens, retry, parser, tools, resolver, logger) { }

            public void SetStream(LLMStreamChunk[] chunks)
                => _chunks = chunks;

            protected override async IAsyncEnumerable<LLMStreamChunk> StreamAsync(
                LLMRequest request,
                [EnumeratorCancellation] CancellationToken ct)
            {
                foreach (var c in _chunks)
                    yield return c;

                await Task.CompletedTask;
            }
        }
    }
}
