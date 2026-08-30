using AgentCore;
using AgentCore.Layers.LLM;

namespace AgentCore.Layers.LLM;

public static class RetryBuilderExtensions
{
    public static Agent.Builder AddRetryLayer(
        this Agent.Builder builder,
        RetryOptions? options = null)
    {
        return builder.AddLLMLayer(new RetryLayer(options));
    }
}
