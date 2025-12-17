using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore.Tests.LLM
{
    using AgentCore.Chat;
    using AgentCore.LLM.Client;
    using AgentCore.LLM.Protocol;
    using AgentCore.LLM.Handlers;
    using AgentCore.Tokens;
    using AgentCore.Tools;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public sealed class LLMClientBase_Tests
    {
        private readonly Mock<IContextManager> _ctx;
        private readonly Mock<ITokenManager> _tokens;
        private readonly Mock<IRetryPolicy> _retry;
        private readonly Mock<IChunkHandler> _handler;
        private readonly ToolRegistryCatalog _tools;
        private readonly ToolCallParser _parser;
        private readonly TestClient _client;

        public LLMClientBase_Tests()
        {
            _ctx = new Mock<IContextManager>();
            _tokens = new Mock<ITokenManager>();
            _retry = new Mock<IRetryPolicy>();
            _handler = new Mock<IChunkHandler>();

            _ctx.Setup(x => x.Trim(It.IsAny<Conversation>(), It.IsAny<int?>()))
                .Returns<Conversation, int?>((c, _) => c);

            _tokens.Setup(x => x.ResolveAndRecord(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TokenUsage>()))
                .Returns(new TokenUsage());

            _tools = new ToolRegistryCatalog();
            _tools.Register((int x) => x + 1);

            _parser = new ToolCallParser(_tools);

            _client = new TestClient(
                new LLMInitOptions(),
                _ctx.Object,
                _tokens.Object,
                _retry.Object,
                _parser,
                _tools,
                _ => _handler.Object,
                Mock.Of<ILogger<LLMClientBase>>()
            );
        }

        [Fact]
        public async Task Single_ToolCall_Is_Detected()
        {
            SetupStream(
                ToolDelta("Invoke", @"{""x"":1}"),
                Finish(FinishReason.ToolCall)
            );

            var resp = await _client.ExecuteAsync<LLMResponse>(
                new LLMRequest(new Conversation()));

            Assert.NotNull(resp.ToolCall);
            Assert.Equal("Invoke", resp.ToolCall!.Name);
        }

        [Fact]
        public async Task Second_ToolCall_Throws_EarlyStop()
        {
            SetupStream(
                ToolDelta("Invoke", @"{""x"":1}"),
                ToolDelta("Invoke", @"{""x"":2}")
            );

            await Assert.ThrowsAsync<EarlyStopException>(() =>
                _client.ExecuteAsync<LLMResponse>(
                    new LLMRequest(new Conversation())));
        }

        [Fact]
        public async Task Invalid_Tool_Throws_RetryException()
        {
            SetupStream(
                ToolDelta("NoSuchTool", @"{}"),
                Finish(FinishReason.ToolCall)
            );

            await Assert.ThrowsAsync<RetryException>(() =>
                _client.ExecuteAsync<LLMResponse>(
                    new LLMRequest(new Conversation())));
        }

        [Fact]
        public async Task Cancellation_Returns_Cancelled_Response()
        {
            SetupStream(); // no chunks

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

            SetupStream(
                new LLMStreamChunk(StreamKind.Text, "hi")
            );

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _client.ExecuteAsync<LLMResponse>(
                    new LLMRequest(new Conversation())));
        }

        // ---------------- helpers ----------------

        private void SetupStream(params LLMStreamChunk[] chunks)
        {
            _retry.Setup(r => r.ExecuteStreamAsync(
                    It.IsAny<Conversation>(),
                    It.IsAny<Func<Conversation, IAsyncEnumerable<LLMStreamChunk>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<Conversation,
                         Func<Conversation, IAsyncEnumerable<LLMStreamChunk>>,
                         CancellationToken>((_, run, __) => run(new Conversation()));

            _client.SetStream(chunks);
        }

        private static LLMStreamChunk ToolDelta(string name, string json)
            => new LLMStreamChunk(
                StreamKind.ToolCallDelta,
                new ToolCallDelta { Name = name, Delta = json });

        private static LLMStreamChunk Finish(FinishReason r)
            => new LLMStreamChunk(StreamKind.Finish, r);

        // ---------------- minimal abstract override ----------------

        private sealed class TestClient : LLMClientBase
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
                ILogger<LLMClientBase> logger)
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
