using AgentCore;
using AgentCore.Layers.LLM;

namespace AgentCore.Layers.LLM;

public static class RetryBuilderExtensions
{
    public static Agent.Builder AddRetryLayer(
        this Agent.Builder builder,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
    {
        return builder.AddLLMLayer(new RetryLayer(
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));
    }
}
