using LlmTornado;
using LlmTornado.Models;
using LlmTornado.Chat.Models;
using LlmTornado.Embedding.Models;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Memory;

namespace AgentCore.Providers.Tornado;

public static class TornadoAgentBuilderExtensions
{
    public static AgentBuilder AddTornado(this AgentBuilder builder, TornadoApi api, string modelName, string? embeddingModelName = null, LLMOptions? options = null)
    {
        var model = new ChatModel(modelName);
        var provider = new TornadoLLMProvider(api, model);
        builder.WithProvider(provider, options);
        builder.WithTokenCounter(new ApproximateTokenCounter());

        return builder;
    }
}
