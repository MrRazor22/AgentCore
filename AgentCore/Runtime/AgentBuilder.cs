using AgentCore.LLM.Execution;
using AgentCore.LLM.Handlers;
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
    public sealed class AgentBuilder
    {
        private string? _defaultSystemPrompt;
        public IServiceCollection Services { get; } = new ServiceCollection();
        private readonly List<Action<ToolRegistryCatalog>> _toolRegistrations = new List<Action<ToolRegistryCatalog>>();
        public AgentBuilder()
        {
            Services.AddSingleton<IAgentMemory, FileMemory>();

            // Tool (SCOPED)
            Services.AddScoped<IToolRuntime, ToolRuntime>();
            Services.AddScoped<IToolCallParser, ToolCallParser>();

            // Token + context
            Services.AddSingleton<ITokenManager, TokenManager>();
            Services.AddSingleton<IContextManager>(sp =>
                new ContextManager(
                    new ContextBudgetOptions(),
                    sp.GetRequiredService<ITokenManager>(),
                    sp.GetRequiredService<ILogger<ContextManager>>()
                )
            );

            // Retry
            Services.AddSingleton<RetryPolicyOptions>();
            Services.AddSingleton<IRetryPolicy, RetryPolicy>();

            // Handlers (SCOPED)
            Services.AddScoped<IChunkHandler, TextHandler>();
            Services.AddScoped<IChunkHandler, ToolCallHandler>();
            Services.AddScoped<IChunkHandler, StructuredHandler>();
            Services.AddScoped<IChunkHandler, FinishHandler>();
            Services.AddScoped<IChunkHandler, TokenUsageHandler>();

            //Logging
            Services.AddLogging();

            //LLM Executor(SCOPED)
            Services.AddScoped<ILLMExecutor, LLMExecutor>();

            //Executor
            Services.AddTransient(typeof(IAgentExecutor<>), typeof(ToolCallingLoop<>));

        }
        public AgentBuilder WithInstructions(string prompt)
        {
            _defaultSystemPrompt = prompt;
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
        public Agent Build() => Build("default");

        public Agent Build(string sessionId)
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
                    "No LLM stream provider registered. Call AddOpenAI() or register an ILLMStreamProvider.");

            return new Agent(provider, sessionId, _defaultSystemPrompt);
        }
    }
    public static class AgentBuilderExtensions
    {
        //Open AI
        public static AgentBuilder AddOpenAI(
    this AgentBuilder builder,
    Action<LLMInitOptions> configure)
        {
            var opts = new LLMInitOptions();
            configure(opts);

            builder.Services.AddSingleton<ITokenCounter>(
                _ => new SharpTokenCounter(opts.Model)
            );

            builder.Services.AddSingleton<ITokenManager, TokenManager>();

            builder.Services.AddSingleton<ILLMStreamProvider>(sp =>
                new OpenAILLMClient(
                    opts,
                    sp.GetRequiredService<ILogger<OpenAILLMClient>>()
                )
            );

            return builder;
        }
        //context trimmer
        public static AgentBuilder AddContextTrimming(
           this AgentBuilder builder,
           Action<ContextBudgetOptions> configure)
        {
            var options = new ContextBudgetOptions();
            configure(options);

            builder.Services.AddSingleton<IContextManager>(sp =>
            {
                return new ContextManager(
                    options,
                    sp.GetRequiredService<ITokenManager>(),
                    sp.GetRequiredService<ILogger<ContextManager>>()
                    );
            });

            return builder;
        }

        // memory config
        public static AgentBuilder AddFileMemory(this AgentBuilder builder, Action<FileMemoryOptions>? configure = null)
        {
            var options = new FileMemoryOptions();
            configure?.Invoke(options);

            builder.Services.AddSingleton<IAgentMemory>(sp => new FileMemory(options));
            return builder;
        }
    }
}
