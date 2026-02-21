using AgentCore.Chat;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Providers;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using Xunit;

namespace AgentCore.Tests.LLM
{
    public sealed class LLMClientBase_Tests
    {
        private readonly Mock<IContextManager> _ctx;
        private readonly Mock<ILLMStreamProvider> _provider;
        private readonly Mock<IRetryPolicy> _retryPolicy;
        private readonly ToolRegistryCatalog _tools;
        private readonly ToolCallParser _parser;

        private static class TestTools
        {
            public static int AddOne(int x) => x + 1;
        }

        public LLMClientBase_Tests()
        {
            _ctx = new Mock<IContextManager>();
            _provider = new Mock<ILLMStreamProvider>();
            _retryPolicy = new Mock<IRetryPolicy>();

            _ctx.Setup(x => x.Trim(It.IsAny<Conversation>(), It.IsAny<int?>()))
                .Returns<Conversation, int?>((c, _) => c);

            _retryPolicy
                .Setup(x => x.ExecuteStreamingAsync(
                    It.IsAny<Conversation>(),
                    It.IsAny<Func<Conversation, IAsyncEnumerable<LLMStreamChunk>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Conversation conv, Func<Conversation, IAsyncEnumerable<LLMStreamChunk>> op, CancellationToken ct) 
                    => op(conv));

            _tools = new ToolRegistryCatalog();
            _tools.Register((Func<int, int>)TestTools.AddOne);

            _parser = new ToolCallParser(_tools);
        }

        private LLMExecutor CreateExecutor(params IChunkHandler[] handlers)
        {
            return new LLMExecutor(
                _provider.Object,
                _retryPolicy.Object,
                handlers,
                _ctx.Object,
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

            var handler = new ToolCallHandler(_tools, _parser, NullLogger<ToolCallHandler>.Instance);
            var executor = CreateExecutor(handler);

            await foreach (var _ in executor.StreamAsync(new LLMRequest(new Conversation())))
            { }

            Assert.NotNull(executor.Response.ToolCall);
            Assert.Equal("TestTools.AddOne", executor.Response.ToolCall!.Name);
        }

        [Fact]
        public async Task Second_ToolCall_Is_Ignored_Not_Thrown()
        {
            SetupStream(
                ToolDelta("TestTools.AddOne", @"{""x"":1}"),
                ToolDelta("TestTools.AddOne", @"{""x"":2}")
            );

            var handler = new ToolCallHandler(_tools, _parser, NullLogger<ToolCallHandler>.Instance);
            var executor = CreateExecutor(handler);

            await foreach (var _ in executor.StreamAsync(new LLMRequest(new Conversation())))
            { }

            Assert.NotNull(executor.Response.ToolCall);
            Assert.Equal("TestTools.AddOne", executor.Response.ToolCall!.Name);
        }

        [Fact]
        public async Task Invalid_Tool_Throws_RetryException()
        {
            SetupStream(
                ToolDelta("NoSuch.Tool", @"{}"),
                Finish(FinishReason.ToolCall)
            );

            var handler = new ToolCallHandler(_tools, _parser, NullLogger<ToolCallHandler>.Instance);
            var executor = CreateExecutor(handler);

            await Assert.ThrowsAsync<RetryException>(async () =>
            {
                await foreach (var _ in executor.StreamAsync(new LLMRequest(new Conversation())))
                { }
            });
        }

        private void SetupStream(params LLMStreamChunk[] chunks)
        {
            _provider.Setup(x => x.StreamAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
                .Returns((LLMRequest _, CancellationToken __) => StreamChunks(chunks));
        }

        private static async IAsyncEnumerable<LLMStreamChunk> StreamChunks(LLMStreamChunk[] chunks)
        {
            foreach (var c in chunks)
                yield return c;

            await Task.CompletedTask;
        }

        private static LLMStreamChunk ToolDelta(string name, string json)
            => new LLMStreamChunk(
                StreamKind.ToolCallDelta,
                new ToolCallDelta { Name = name, Delta = json });

        private static LLMStreamChunk Finish(FinishReason r)
            => new LLMStreamChunk(StreamKind.Finish, r);
    }
}
