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
        Conversation ScratchPad { get; }
        string? UserRequest { get; }
        IServiceProvider Services { get; }
        Action<LLMStreamChunk>? Stream { get; }
        CancellationToken CancellationToken { get; }
    }
    public sealed class AgentContext : IAgentContext
    {
        public AgentContext(
            IServiceProvider services,
            CancellationToken ct)
        {
            Services = services;
            CancellationToken = ct;
            ScratchPad = new Conversation();
        }

        public Conversation ScratchPad { get; }
        public string? UserRequest { get; set; }
        public IServiceProvider Services { get; }
        public Action<LLMStreamChunk>? Stream { get; set; }
        public CancellationToken CancellationToken { get; }
    }

    public interface IAgent
    {
        string SessionId { get; }

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
        private string? _systemPrompt;

        public string SessionId { get; }

        internal Agent(IServiceProvider services, string sessionId, string? systemPrompt)
        {
            _services = services;
            SessionId = sessionId;
            _systemPrompt = systemPrompt;
            _memory = services.GetRequiredService<IAgentMemory>();
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

            var ctx = new AgentContext(scope.ServiceProvider, ct)
            {
                UserRequest = goal,
                Stream = stream
            };

            if (!string.IsNullOrEmpty(_systemPrompt))
                ctx.ScratchPad.AddSystem(_systemPrompt);

            var memory = await _memory.RecallAsync(SessionId, goal).ConfigureAwait(false);
            if (memory != null)
                ctx.ScratchPad.Append(memory);

            var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor<T>>();
            var response = await executor.ExecuteAsync(ctx).ConfigureAwait(false);

            await _memory.UpdateAsync(SessionId, goal, response.Result?.ToString())
                     .ConfigureAwait(false);

            return response.Result;
        }
    }

}