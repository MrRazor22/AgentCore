using AgentCore.LLMCore.Client;
using AgentCore.LLMCore.Pipeline;
using AgentCore.Providers.OpenAI;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace AgentCore.Runtime
{
    public static class AgentBuilderExtensions
    {
        //Open AI
        public static AgentBuilder AddOpenAI(this AgentBuilder builder, Action<LLMInitOptions> configure)
        {
            var opts = new LLMInitOptions();
            configure(opts);

            builder.Services.AddSingleton<IToolRuntime>(sp =>
            {
                var registry = sp.GetRequiredService<IToolCatalog>();
                return new ToolRuntime(registry);
            });

            builder.Services.AddSingleton<ILLMClient>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ILLMClient>>();
                var registry = sp.GetRequiredService<IToolCatalog>();
                var estimator = sp.GetRequiredService<ITokenEstimator>();
                var ctxManager = sp.GetRequiredService<IContextBudgetManager>();
                var tokenManager = sp.GetRequiredService<ITokenManager>();
                var retry = sp.GetRequiredService<IRetryPolicy>();
                var parser = sp.GetRequiredService<IToolCallParser>();

                return new OpenAILLMClient(
                    opts,
                    registry,
                    estimator,
                    ctxManager,
                    tokenManager,
                    retry,
                    parser,
                    logger);
            });

            return builder;
        }

        //Retry policy 
        public static AgentBuilder AddRetryPolicy(this AgentBuilder builder, Action<RetryPolicyOptions>? configure = null)
        {
            if (configure != null)
                builder.Services.Configure(configure);
            else
                builder.Services.Configure<RetryPolicyOptions>(_ => { }); // defaults

            builder.Services.AddSingleton<IRetryPolicy, RetryPolicy>();
            return builder;
        }

        //context trimmer
        public static AgentBuilder AddContextTrimming(
           this AgentBuilder builder,
           Action<ContextBudgetOptions> configure)
        {
            var options = new ContextBudgetOptions();
            configure(options);

            builder.Services.AddSingleton<IContextBudgetManager>(sp =>
            {
                var tokenizer = sp.GetRequiredService<ITokenizer>();
                return new ContextBudgetManager(options, tokenizer, sp.GetRequiredService<ITokenEstimator>());
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
