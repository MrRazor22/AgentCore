using AgentCore.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    public interface IAgentContext
    {
        AgentConfig Config { get; }
        Conversation ScratchPad { get; }
        string UserInput { get; }
        IServiceProvider Services { get; }
        CancellationToken CancellationToken { get; }
    }

    public sealed class AgentContext : IAgentContext
    {
        public AgentContext(
            AgentConfig config,
            IServiceProvider services,
            string userInput,
            CancellationToken ct)
        {
            Config = config;
            Services = services;
            UserInput = userInput;
            CancellationToken = ct;
            ScratchPad = new Conversation();
        }

        public AgentConfig Config { get; }
        public Conversation ScratchPad { get; }
        public string UserInput { get; }
        public IServiceProvider Services { get; }
        public CancellationToken CancellationToken { get; }
    }

    public interface IAgent
    {
        Task<string> InvokeAsync(string input, CancellationToken ct = default);
        IAsyncEnumerable<string> InvokeStreamingAsync(string input, CancellationToken ct = default);
    }

    public sealed class LLMAgent : IAgent
    {
        private readonly IServiceProvider _services;
        private readonly IAgentMemory _memory;
        private readonly ILogger<LLMAgent> _logger;
        private readonly AgentConfig _config;

        public static AgentBuilder Create(string name = "agent")
        {
            return new AgentBuilder()
                .WithName(name);
        }

        internal LLMAgent(IServiceProvider services, AgentConfig config)
        {
            _config = config;
            _services = services;
            _memory = services.GetRequiredService<IAgentMemory>();
            _logger = services.GetRequiredService<ILogger<LLMAgent>>();
        }

        public async Task<string> InvokeAsync(string input, CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            await foreach (var chunk in InvokeStreamingAsync(input, ct))
            {
                sb.Append(chunk);
            }
            return sb.ToString();
        }

        public async IAsyncEnumerable<string> InvokeStreamingAsync(
            string input,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using var scope = _services.CreateScope();
            var sessionId = System.Guid.NewGuid().ToString("N");

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Agent"] = _config.Name,
                ["Session"] = sessionId
            }))
            {
                var ctx = new AgentContext(
                    _config,
                    scope.ServiceProvider,
                    input,
                    ct
                );

                ctx.ScratchPad.AddSystem(_config.SystemPrompt);

                var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();

                var sb = new StringBuilder();
                await foreach (var chunk in executor.ExecuteStreamingAsync(ctx, ct))
                {
                    sb.Append(chunk);
                    yield return chunk;
                }

                await _memory.UpdateAsync(
                    sessionId,
                    input,
                    sb.ToString()
                );
            }
        }
    }
}
