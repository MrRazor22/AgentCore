using AgentCore.Chat;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Runtime;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace AgentCore.Tests.Runtime
{
    public sealed class ToolCallingLoop_Tests
    {
        [Fact]
        public async Task Executes_Once_When_No_ToolCall()
        {
            var llm = new Mock<ILLMExecutor>();
            var runtime = new Mock<IToolRuntime>();
            var tools = new Mock<IToolCatalog>();

            var response = new LLMResponse { Text = "done", FinishReason = FinishReason.Stop };

            llm.Setup(x => x.Response).Returns(response);
            llm.Setup(x => x.StreamAsync(
                    It.IsAny<LLMRequest>(),
                    It.IsAny<CancellationToken>()))
               .Returns((LLMRequest _, CancellationToken __) => 
                   StreamText("done"));

            var services = BuildServices(llm, runtime, tools);
            var ctx = CreateContext(services, "hi");

            var loop = new ToolCallingLoop(NullLogger<ToolCallingLoop>.Instance);
            var result = await CollectAsync(loop.ExecuteStreamingAsync(ctx));

            Assert.Single(result);
            Assert.Equal("done", result[0]);
        }

        [Fact]
        public async Task ToolCall_Is_Executed_Then_Loop_Stops()
        {
            var llm = new Mock<ILLMExecutor>();
            var runtime = new Mock<IToolRuntime>();
            var tools = new Mock<IToolCatalog>();

            var toolCall = new ToolCall("1", "Sum", null!);

            var callCount = 0;
            llm.Setup(x => x.Response)
               .Returns(() => callCount == 1 
                   ? new LLMResponse { ToolCall = toolCall, FinishReason = FinishReason.ToolCall }
                   : new LLMResponse { Text = "final", FinishReason = FinishReason.Stop });
            
            llm.Setup(x => x.StreamAsync(
                    It.IsAny<LLMRequest>(),
                    It.IsAny<CancellationToken>()))
               .Returns((LLMRequest _, CancellationToken __) =>
               {
                   callCount++;
                   if (callCount == 1)
                       return StreamEmpty();
                   else
                       return StreamText("final");
               });

            runtime.Setup(x => x.HandleToolCallAsync(
                    toolCall,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolCallResult(toolCall, 3));

            tools.Setup(x => x.RegisteredTools).Returns(new List<Tool>());

            var services = BuildServices(llm, runtime, tools);
            var ctx = CreateContext(services, "calc");

            var loop = new ToolCallingLoop(NullLogger<ToolCallingLoop>.Instance);
            var result = await CollectAsync(loop.ExecuteStreamingAsync(ctx));

            Assert.Equal("final", string.Join("", result));
            runtime.Verify(x => x.HandleToolCallAsync(toolCall, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Respects_MaxIterations()
        {
            var llm = new Mock<ILLMExecutor>();
            var runtime = new Mock<IToolRuntime>();
            var tools = new Mock<IToolCatalog>();

            var toolCall = new ToolCall("1", "Loop", null!);

            llm.Setup(x => x.Response)
               .Returns(new LLMResponse { ToolCall = toolCall, FinishReason = FinishReason.ToolCall });

            llm.Setup(x => x.StreamAsync(
                    It.IsAny<LLMRequest>(),
                    It.IsAny<CancellationToken>()))
               .Returns((LLMRequest _, CancellationToken __) => StreamEmpty());

            runtime.Setup(x => x.HandleToolCallAsync(
                    toolCall,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolCallResult(toolCall, null));

            tools.Setup(x => x.RegisteredTools).Returns(new List<Tool>());

            var services = BuildServices(llm, runtime, tools);
            var config = new AgentConfig { MaxIterations = 3 };
            var ctx = CreateContext(services, "loop", config: config);

            var loop = new ToolCallingLoop(NullLogger<ToolCallingLoop>.Instance);
            await CollectAsync(loop.ExecuteStreamingAsync(ctx));

            llm.Verify(x => x.StreamAsync(
                It.IsAny<LLMRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        // -------- helpers --------

        private static IServiceProvider BuildServices(
            Mock<ILLMExecutor> llm,
            Mock<IToolRuntime> runtime,
            Mock<IToolCatalog> tools)
        {
            return new ServiceCollection()
                .AddSingleton(llm.Object)
                .AddSingleton(runtime.Object)
                .AddSingleton(tools.Object)
                .BuildServiceProvider();
        }

        private static AgentContext CreateContext(
            IServiceProvider services, 
            string userInput,
            AgentConfig? config = null)
        {
            return new AgentContext(
                config ?? new AgentConfig(),
                services,
                userInput,
                CancellationToken.None);
        }

        private static async IAsyncEnumerable<LLMStreamChunk> StreamText(string text)
        {
            yield return new LLMStreamChunk(StreamKind.Text, text);
            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<LLMStreamChunk> StreamEmpty()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
        {
            var result = new List<string>();
            await foreach (var item in source)
            {
                result.Add(item);
            }
            return result;
        }
    }
}
