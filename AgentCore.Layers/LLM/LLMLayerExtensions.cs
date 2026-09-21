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
        return llm.AddLayer(new RetryLayer(
            llm,
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));
    }

    public static ILLM RemoveRetry(this ILLM llm)
        => llm.RemoveLayer<RetryLayer>();

    public static ILLM UseToolCallDetection(this ILLM llm, bool stopAfterFirstToolCall = false)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return llm.AddLayer(new ToolCallDetectionLayer(stopAfterFirstToolCall, llm));
    }

    public static ILLM RemoveToolCallDetection(this ILLM llm)
        => llm.RemoveLayer<ToolCallDetectionLayer>();

    public static ILLM UseInputGuardrail(this ILLM llm, InputGuardrail guardrail)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(guardrail);
        return llm.AddLayer(new InputGuardrailLayer(guardrail, llm));
    }

    public static ILLM RemoveInputGuardrail(this ILLM llm)
        => llm.RemoveLayer<InputGuardrailLayer>();
}
