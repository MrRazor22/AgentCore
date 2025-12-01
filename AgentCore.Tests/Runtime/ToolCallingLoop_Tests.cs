using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.Runtime;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Tests.Runtime
{
    public class ToolCallingLoop_Tests
    {
        [Fact]
        public async Task Loop_RunsTool_AndStops()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILLMClient, FakeLLM>();
            services.AddSingleton<IToolRuntime, FakeRuntime>();

            var ctx = new AgentContext(services.BuildServiceProvider());
            ctx.UserRequest = "x";

            var loop = new ToolCallingLoop();
            await loop.ExecuteAsync(ctx);

            Assert.Equal("done", ctx.Response.Message);
        }

        class FakeLLM : ILLMClient
        {
            public Task<LLMResponse> ExecuteAsync(LLMRequest r, CancellationToken ct = default, Action<LLMStreamChunk> s = null)
            {
                return Task.FromResult(new LLMResponse(null, new ToolCall("id", "Do", new Newtonsoft.Json.Linq.JObject()), "stop", null));
            }
            public Task<LLMStructuredResponse> ExecuteAsync(LLMStructuredRequest r, CancellationToken ct = default, Action<LLMStreamChunk> s = null)
                => throw new System.NotImplementedException();
        }

        class FakeRuntime : IToolRuntime
        {
            public Task<object> InvokeAsync(ToolCall c, CancellationToken ct = default)
                => Task.FromResult((object)"done");

            public Task<ToolCallResult> HandleToolCallAsync(ToolCall c, CancellationToken ct = default)
                => Task.FromResult(new ToolCallResult(c, "done"));
        }
    }
}
