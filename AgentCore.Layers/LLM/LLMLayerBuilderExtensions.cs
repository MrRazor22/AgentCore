using AgentCore;
using AgentCore.LLM;

namespace AgentCore.Layers.LLM;

public static class LLMLayerBuilderExtensions
{
    public static LLMBuilder WithToolCallDetection(this LLMBuilder builder, bool stopAfterFirstToolCall = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ToolCallDetectionLayer(stopAfterFirstToolCall));
    }

    public static AgentBuilder AddToolCallDetection(this AgentBuilder builder, bool stopAfterFirstToolCall = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithToolCallDetection(stopAfterFirstToolCall));
    }

    public static LLMBuilder WithMessageCoalescing(this LLMBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new MessageCoalescingLayer());
    }

    public static AgentBuilder AddMessageCoalescing(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithMessageCoalescing());
    }

    public static LLMBuilder WithStreamingEvents<T>(this LLMBuilder builder, Func<IMessageEvent, T>? mapper = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new StreamingEventLayer<T>(mapper));
    }

    public static AgentBuilder AddStreamingEvents<T>(this AgentBuilder builder, Func<IMessageEvent, T>? mapper = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithStreamingEvents(mapper));
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
