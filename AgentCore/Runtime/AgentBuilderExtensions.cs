using AgentCore.LLM.Client;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Pipeline;
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
        public static AgentBuilder AddOpenAI(
            this AgentBuilder builder,
            Action<LLMInitOptions> configure)
        {
            var opts = new LLMInitOptions();
            configure(opts);

            builder.Services.AddTransient<TextToolCallHandler>();
            builder.Services.AddTransient<StructuredHandler>();

            builder.Services.AddSingleton<TextHandlerFactory>(sp =>
            {
                return () => sp.GetRequiredService<TextToolCallHandler>();
            });

            builder.Services.AddSingleton<StructuredHandlerFactory>(sp =>
            {
                return () => sp.GetRequiredService<StructuredHandler>();
            });

            builder.Services.AddSingleton<ILLMClient>(sp =>
            {
                return new OpenAILLMClient(
                    opts,
                    sp.GetRequiredService<ILLMPipeline>(),
                    sp.GetRequiredService<TextHandlerFactory>(),
                    sp.GetRequiredService<StructuredHandlerFactory>(),
                    sp.GetRequiredService<ILogger<ILLMClient>>()
                );
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
                return new ContextBudgetManager(options, sp.GetRequiredService<ITokenManager>());
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
