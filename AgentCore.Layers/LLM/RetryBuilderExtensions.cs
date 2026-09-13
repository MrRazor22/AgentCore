using AgentCore;
using AgentCore.LLM;
using AgentCore.Layers.LLM;

namespace AgentCore.Layers.LLM;

public static class RetryBuilderExtensions
{
    public static LLMBuilder WithRetry(
        this LLMBuilder builder,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new RetryLayer(
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));
    }

    public static AgentBuilder AddRetryLayer(
        this AgentBuilder builder,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithRetry(
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));
    }
}
