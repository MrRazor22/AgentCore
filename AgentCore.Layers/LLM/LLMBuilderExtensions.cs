using AgentCore.LLM;

namespace AgentCore.Layers.LLM;

public static class LLMBuilderExtensions
{

    public static LLMBuilder WithToolCallDetection(this LLMBuilder builder, bool stopAfterFirstToolCall = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ToolCallDetectionLayer(stopAfterFirstToolCall));
    }

    public static LLMBuilder WithStreamingEvents<T>(this LLMBuilder builder, Func<IMessageEvent, T>? mapper = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new StreamingEventLayer<T>(mapper));
    }

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
}
