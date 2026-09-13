using AgentCore;
using AgentCore.LLM;

namespace AgentCore.Layers.LLM;

public static class LLMLayerBuilderExtensions
{
    public static LLMBuilder WithToolCallDetection(this LLMBuilder builder, ToolCallDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new ToolCallDetectionLayer(options));
    }

    public static AgentBuilder AddToolCallDetection(this AgentBuilder builder, ToolCallDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithToolCallDetection(options));
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

    public static LLMBuilder WithStreamingEvents<T>(this LLMBuilder builder, Func<IAgentEvent, T>? mapper = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddLayer(new StreamingEventLayer<T>(mapper));
    }

    public static AgentBuilder AddStreamingEvents<T>(this AgentBuilder builder, Func<IAgentEvent, T>? mapper = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithStreamingEvents(mapper));
    }
}
