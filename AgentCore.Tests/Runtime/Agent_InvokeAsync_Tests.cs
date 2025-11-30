using AgentCore.Chat;
using AgentCore.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Tests.Runtime
{
    public class Agent_InvokeAsync_Tests
    {
        [Fact]
        public async Task InvokeAsync_UsesExecutor_AndUpdatesMemory()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAgentMemory, FakeMemory>();
            services.AddSingleton<IAgentExecutor, FakeExecutor>();

            // Since constructor is internal, InternalsVisibleTo is required
            var provider = services.BuildServiceProvider();
            var agent = new Agent(provider, "test");

            agent.UseExecutor(() => provider.GetRequiredService<IAgentExecutor>());

            var response = await agent.InvokeAsync("hello");

            Assert.Equal("executor-output", response.Message);

            var mem = provider.GetRequiredService<IAgentMemory>() as FakeMemory;
            Assert.Contains("hello", mem.Data);
            Assert.Contains("executor-output", mem.Data);
        }

        class FakeExecutor : IAgentExecutor
        {
            public Task ExecuteAsync(IAgentContext ctx)
            {
                ctx.Response.Set("executor-output");
                return Task.CompletedTask;
            }
        }

        class FakeMemory : IAgentMemory
        {
            public string Data = "";

            public Task<Conversation> RecallAsync(string sessionId, string userRequest)
            {
                return Task.FromResult(new Conversation());
            }

            public Task UpdateAsync(string sessionId, string userRequest, string response)
            {
                Data += $"{userRequest}|{response}";
                return Task.CompletedTask;
            }
        }
    }
}
