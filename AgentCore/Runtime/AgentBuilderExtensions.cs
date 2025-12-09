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

            builder.Services.AddSingleton<HandlerResolver>(sp => request =>
            {
                return request switch
                {
                    LLMTextRequest _ => sp.GetRequiredService<TextHandler>(),
                    LLMStructuredRequest _ => sp.GetRequiredService<StructuredHandler>(),
                    _ => throw new NotSupportedException(
                        $"Unsupported LLM request type: {request.GetType().Name}")
                };
            });

            builder.Services.AddSingleton<ILLMClient>(sp =>
            {
                return new OpenAILLMClient(
                    opts,
                    sp.GetRequiredService<ILLMPipeline>(),
                    sp.GetRequiredService<HandlerResolver>()
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
                return new ContextBudgetManager(
                    options,
                    sp.GetRequiredService<ITokenManager>(),
                    sp.GetRequiredService<ILogger<ContextBudgetManager>>()
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
