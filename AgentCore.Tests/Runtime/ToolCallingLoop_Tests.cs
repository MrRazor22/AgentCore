using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.LLM.Protocol;
using AgentCore.Runtime;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentCore.Tests.Runtime
{
    public sealed class ToolCallingLoop_Tests
    {
        [Fact]
        public async Task Executes_Once_When_No_ToolCall()
        {
            var llm = new Mock<ILLMClient>();
            var runtime = new Mock<IToolRuntime>();

            llm.Setup(x => x.ExecuteAsync<LLMResponse>(
                    It.IsAny<LLMRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Action<LLMStreamChunk>>()))
               .ReturnsAsync(new LLMResponse(
                   assistantMessage: "done",
                   toolCall: null,
                   finishReason: FinishReason.Stop));

            var services = BuildServices(llm, runtime);
            var ctx = new AgentContext(services)
            {
                UserRequest = "hi"
            };

            var loop = new ToolCallingLoop();
            await loop.ExecuteAsync(ctx);

            Assert.Equal("done", ctx.Response.Message);
            llm.Verify(x => x.ExecuteAsync<LLMResponse>(
                It.IsAny<LLMRequest>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<LLMStreamChunk>>()), Times.Once);
        }

        [Fact]
        public async Task ToolCall_Is_Executed_Then_Loop_Stops()
        {
            var llm = new Mock<ILLMClient>();
            var runtime = new Mock<IToolRuntime>();

            var toolCall = new ToolCall("1", "Sum", null!);

            llm.SetupSequence(x => x.ExecuteAsync<LLMResponse>(
                    It.IsAny<LLMRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Action<LLMStreamChunk>>()))
               .ReturnsAsync(new LLMResponse(null, toolCall, FinishReason.ToolCall))
               .ReturnsAsync(new LLMResponse("final", null, FinishReason.Stop));

            runtime.Setup(x => x.HandleToolCallAsync(
                    toolCall,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolCallResult(toolCall, 3));

            var services = BuildServices(llm, runtime);
            var ctx = new AgentContext(services)
            {
                UserRequest = "calc"
            };

            var loop = new ToolCallingLoop();
            await loop.ExecuteAsync(ctx);

            Assert.Equal("final", ctx.Response.Message);
            runtime.Verify(x => x.HandleToolCallAsync(toolCall, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Streaming_Text_Is_Forwarded_To_Context_Stream()
        {
            var llm = new Mock<ILLMClient>();
            var runtime = new Mock<IToolRuntime>();

            llm.Setup(x => x.ExecuteAsync<LLMResponse>(
                    It.IsAny<LLMRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Action<LLMStreamChunk>>()))
               .Callback<LLMRequest, CancellationToken, Action<LLMStreamChunk>>(
                   (_, __, stream) =>
                   {
                       stream(new LLMStreamChunk(StreamKind.Text, "hi"));
                       stream(new LLMStreamChunk(StreamKind.Text, " there"));
                   })
               .ReturnsAsync(new LLMResponse("hi there", null, FinishReason.Stop));

            var streamed = new List<object>();
            var services = BuildServices(llm, runtime);
            var ctx = new AgentContext(services)
            {
                UserRequest = "hello",
                Stream = streamed.Add
            };

            var loop = new ToolCallingLoop();
            await loop.ExecuteAsync(ctx);

            Assert.Equal(new[] { "hi", " there" }, streamed);
        }

        [Fact]
        public async Task Respects_MaxIterations()
        {
            var llm = new Mock<ILLMClient>();
            var runtime = new Mock<IToolRuntime>();

            var toolCall = new ToolCall("1", "Loop", null!);

            llm.Setup(x => x.ExecuteAsync<LLMResponse>(
                    It.IsAny<LLMRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Action<LLMStreamChunk>>()))
               .ReturnsAsync(new LLMResponse(null, toolCall, FinishReason.ToolCall));

            runtime.Setup(x => x.HandleToolCallAsync(
                    toolCall,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolCallResult(toolCall, null));

            var services = BuildServices(llm, runtime);
            var ctx = new AgentContext(services)
            {
                UserRequest = "loop"
            };

            var loop = new ToolCallingLoop(maxIterations: 3);
            await loop.ExecuteAsync(ctx);

            llm.Verify(x => x.ExecuteAsync<LLMResponse>(
                It.IsAny<LLMRequest>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<LLMStreamChunk>>()), Times.Exactly(3));
        }

        [Fact]
        public async Task Stops_When_Cancellation_Is_Requested()
        {
            var llm = new Mock<ILLMClient>();
            var runtime = new Mock<IToolRuntime>();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var services = BuildServices(llm, runtime);
            var ctx = new AgentContext(services, cts.Token)
            {
                UserRequest = "cancel"
            };

            var loop = new ToolCallingLoop();
            await loop.ExecuteAsync(ctx);

            llm.VerifyNoOtherCalls();
        }

        // -------- helpers --------

        private static IServiceProvider BuildServices(
            Mock<ILLMClient> llm,
            Mock<IToolRuntime> runtime)
        {
            return new ServiceCollection()
                .AddSingleton(llm.Object)
                .AddSingleton(runtime.Object)
                .BuildServiceProvider();
        }
    }
}
