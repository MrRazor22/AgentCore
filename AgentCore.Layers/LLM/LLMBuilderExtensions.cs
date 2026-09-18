using AgentCore;
using AgentCore.LLM;

namespace AgentCore.Layers.LLM;

public static class LLMExtensions
{
    public static ILLM WithToolCallDetection(this ILLM llm, bool stopAfterFirstToolCall = false)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return new ToolCallDetectionLayer(stopAfterFirstToolCall, llm);
    }

    public static ILLM WithStreamingEvents<T>(this ILLM llm, Func<IMessageEvent, T>? mapper = null)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return new StreamingEventLayer<T>(mapper, llm);
    }

    public static ILLM WithRetry(
       this ILLM llm,
       int maxRetries = 3,
       TimeSpan? initialDelay = null,
       TimeSpan? maxDelay = null,
       double backoffMultiplier = 2.0,
       bool useJitter = true,
       Func<Exception, int, bool>? shouldRetry = null,
       Action<Exception, int, TimeSpan>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return new RetryLayer(
            llm,
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry);
    }

    public static Agent UseRetry(
        this Agent agent,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
        => agent.AddLayer(new RetryLayer(
            agent.LLM,
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));

    public static Agent RemoveRetry(this Agent agent)
        => agent.RemoveLayer<RetryLayer>();

    public static Agent UseToolCallDetection(this Agent agent, bool stopAfterFirstToolCall = false)
        => agent.AddLayer(new ToolCallDetectionLayer(stopAfterFirstToolCall, agent.LLM));

    public static Agent RemoveToolCallDetection(this Agent agent)
        => agent.RemoveLayer<ToolCallDetectionLayer>();

    public static Agent UseStreamingEvents<T>(this Agent agent, Func<IMessageEvent, T>? mapper = null)
        => agent.AddLayer(new StreamingEventLayer<T>(mapper, agent.LLM));

    public static Agent WithRetry(
        this Agent agent,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
        => agent.UseRetry(maxRetries, initialDelay, maxDelay, backoffMultiplier, useJitter, shouldRetry, onRetry);

    public static Agent WithoutRetry(this Agent agent)
        => agent.RemoveRetry();

    public static Agent WithToolCallDetection(this Agent agent, bool stopAfterFirstToolCall = false)
        => agent.UseToolCallDetection(stopAfterFirstToolCall);

    public static Agent WithoutToolCallDetection(this Agent agent)
        => agent.RemoveToolCallDetection();

    public static Agent WithStreamingEvents<T>(this Agent agent, Func<IMessageEvent, T>? mapper = null)
        => agent.UseStreamingEvents<T>(mapper);
}
