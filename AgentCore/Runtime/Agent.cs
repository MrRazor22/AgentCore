using AgentCore.Chat;
using AgentCore.LLM.Protocol;
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
        string UserInput { get; }
        IServiceProvider Services { get; }
        Action<LLMStreamChunk>? Stream { get; }
        CancellationToken CancellationToken { get; }
    }
    public sealed class AgentContext : IAgentContext
    {
        public AgentContext(
            AgentConfig config,
            IServiceProvider services,
            string userInput,
            CancellationToken ct,
            Action<LLMStreamChunk>? stream)
        {
            Config = config;
            Services = services;
            UserInput = userInput;
            CancellationToken = ct;
            Stream = stream;
            ScratchPad = new Conversation();
        }

        public AgentConfig Config { get; }
        public Conversation ScratchPad { get; }
        public string UserInput { get; }
        public IServiceProvider Services { get; }
        public CancellationToken CancellationToken { get; }
        public Action<LLMStreamChunk>? Stream { get; }
    }

    public sealed class AgentResponse
    {
        public string? Text { get; set; }
        public object? Output { get; set; }
    }

    public interface IAgent
    {
        Task<AgentResponse> InvokeAsync(
            string userInput,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? stream = null);
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

        public async Task<AgentResponse> InvokeAsync(
            string userInput,
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
                    userInput,
                    ct,
                    stream
                );

                ctx.ScratchPad.AddSystem(_config.SystemPrompt);


                var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();
                var llmResponse = await executor.ExecuteAsync(ctx);

                // memory update
                await _memory.UpdateAsync(
                    sessionId,
                    userInput,
                    llmResponse.Text ?? llmResponse.Output?.ToString() ?? string.Empty
                );

                return new AgentResponse
                {
                    Text = llmResponse.Text,
                    Output = llmResponse.Output,
                };
            }
        }
    }
}