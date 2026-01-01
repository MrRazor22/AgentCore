using AgentCore.LLM.Execution;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Providers;
using AgentCore.Providers.OpenAI;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace AgentCore.Runtime
{
    // === 2. AgentBuilder ===
    public sealed class AgentConfig
    {
        public string Name { get; set; } = "agent";
        public string? SystemPrompt { get; set; }
        public string? Model { get; set; }
        public LLMGenerationOptions Generation { get; set; } = new LLMGenerationOptions();
        public int MaxIterations { get; set; } = 50;
    }
    public sealed class AgentBuilder
    {
        private readonly AgentConfig _config = new AgentConfig();
        private readonly List<Action<ToolRegistryCatalog>> _toolRegistrations = new List<Action<ToolRegistryCatalog>>();
        public IServiceCollection Services { get; } = new ServiceCollection();

        public AgentBuilder()
        {
            Services.AddLogging();

            // === CORE INFRA (ALWAYS PRESENT) ===

            // Memory (default impl, configurable)
            Services.AddSingleton<IAgentMemory, FileMemory>();

            // Context trimming (default impl, configurable)
            Services.AddSingleton<IContextManager, ContextManager>();

            // Tokens
            Services.AddSingleton<ITokenManager, TokenManager>();

            // Retry
            Services.AddSingleton<IRetryPolicy, RetryPolicy>();

            // Tools
            Services.AddScoped<IToolRuntime, ToolRuntime>();
            Services.AddScoped<IToolCallParser, ToolCallParser>();

            // Handlers
            Services.AddScoped<IChunkHandler, TextHandler>();
            Services.AddScoped<IChunkHandler, ToolCallHandler>();
            Services.AddScoped<IChunkHandler, StructuredHandler>();
            Services.AddScoped<IChunkHandler, FinishHandler>();
            Services.AddScoped<IChunkHandler, TokenUsageHandler>();

            // LLM executor
            Services.AddScoped<ILLMExecutor, LLMExecutor>();

            // Agent executor
            Services.AddTransient(typeof(IAgentExecutor<>), typeof(ToolCallingLoop<>));
        }

        public AgentBuilder WithName(string name)
        {
            _config.Name = name;
            return this;
        }

        public AgentBuilder WithInstructions(string prompt)
        {
            _config.SystemPrompt = prompt;
            return this;
        }

        public AgentBuilder WithModel(string model)
        {
            _config.Model = model;
            return this;
        }

        public AgentBuilder WithTools<T>()
        {
            _toolRegistrations.Add(r => r.RegisterAll<T>());
            return this;
        }

        public AgentBuilder WithTools<T>(T instance)
        {
            _toolRegistrations.Add(r => r.RegisterAll(instance));
            return this;
        }

        public Agent Build()
        {
            Services.AddScoped<ToolRegistryCatalog>(sp =>
            {
                var reg = new ToolRegistryCatalog();
                foreach (var init in _toolRegistrations)
                    init(reg);
                return reg;
            });

            Services.AddScoped<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
            Services.AddScoped<IToolCatalog>(sp => sp.GetRequiredService<ToolRegistryCatalog>());

            var provider = Services.BuildServiceProvider(validateScopes: true);

            if (provider.GetService<ILLMStreamProvider>() == null)
                throw new InvalidOperationException(
                    "No LLM provider registered. Call AddOpenAI() or equivalent.");

            return new Agent(provider, _config);
        }
    }

    public static class AgentBuilderExtensions
    {
        //Open AI
        public static AgentBuilder AddOpenAI(
            this AgentBuilder builder,
            Action<LLMInitOptions> configure)
        {
            builder.Services.Configure(configure);
            builder.Services.AddSingleton<ILLMStreamProvider, OpenAILLMClient>();
            return builder;
        }
    }
}
