using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.LLM;

public static class LLMLayerExtensions
{
    public static ILLM UseRetry(
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
        return llm.Add(new RetryLayer(
            llm,
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));
    }

    public static ILLM UseToolCallDetection(this ILLM llm, bool stopAfterFirstToolCall = false)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return llm.Add(new ToolCallDetectionLayer(stopAfterFirstToolCall, llm));
    }

    public static ILLM UseInputGuardrail(this ILLM llm, InputGuardrail guardrail)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(guardrail);
        return llm.Add(new InputGuardrailLayer(guardrail, llm));
    }
}
