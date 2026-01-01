using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Execution;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    public interface IAgentContext
    {
        AgentConfig Config { get; }
        Conversation ScratchPad { get; }
        string? UserRequest { get; }
        IServiceProvider Services { get; }
        Action<LLMStreamChunk>? Stream { get; }
        CancellationToken CancellationToken { get; }
    }
    public sealed class AgentContext : IAgentContext
    {
        public AgentContext(
            AgentConfig config,
            IServiceProvider services,
            string userRequest,
            CancellationToken ct,
            Action<LLMStreamChunk>? stream)
        {
            Config = config;
            Services = services;
            UserRequest = userRequest;
            CancellationToken = ct;
            Stream = stream;
            ScratchPad = new Conversation();
        }

        public AgentConfig Config { get; }
        public Conversation ScratchPad { get; }
        public string UserRequest { get; }
        public IServiceProvider Services { get; }
        public CancellationToken CancellationToken { get; }
        public Action<LLMStreamChunk>? Stream { get; }
    }

    public interface IAgent
    {
        Task<string> InvokeAsync(
            string goal,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? stream = null);

        Task<T> InvokeAsync<T>(
            string goal,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? stream = null);
    }

    public sealed class Agent : IAgent
    {
        private readonly IServiceProvider _services;
        private readonly IAgentMemory _memory;
        private readonly ILogger<Agent> _logger;
        private readonly AgentConfig _config;
        public static AgentBuilder Create(string name = "agent")
        {
            return new AgentBuilder()
                .WithName(name);
        }

        internal Agent(IServiceProvider services, AgentConfig config)
        {
            _config = config;
            _services = services;
            _memory = services.GetRequiredService<IAgentMemory>();
            _logger = services.GetRequiredService<ILogger<Agent>>();
        }

        public Task<string> InvokeAsync(
            string goal,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? stream = null)
            => InvokeAsync<string>(goal, ct, stream);
        public async Task<T> InvokeAsync<T>(
            string goal,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? stream = null)
        {
            using var scope = _services.CreateScope();
            var sessionId = Guid.NewGuid().ToString("N");
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Agent"] = _config.Name,
                ["Session"] = sessionId
            }))
            {
                var ctx = new AgentContext(
                    _config,
                    scope.ServiceProvider,
                    goal,
                    ct,
                    stream
                );

                if (!string.IsNullOrEmpty(_config.SystemPrompt))
                    ctx.ScratchPad.AddSystem(_config.SystemPrompt);

                await _memory.RecallAsync(sessionId, goal, ctx.ScratchPad);

                var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor<T>>();
                var response = await executor.ExecuteAsync(ctx);

                await _memory.UpdateAsync(sessionId, goal, response.Result?.ToString());

                return response.Result;
            }
        }
    }
}