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

    public static Agent WithRetry(
        this Agent agent,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.WithLLM(agent.LLM.WithRetry(
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));
    }
}
