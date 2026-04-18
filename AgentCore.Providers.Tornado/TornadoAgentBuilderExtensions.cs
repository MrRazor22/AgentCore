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

    public static AgentBuilder AddTornadoLLMProvider(this AgentBuilder builder, string apiKey, string modelName, string? baseUrl = null, LLMOptions? options = null)
    {
        var api = new TornadoApi(apiKey, baseUrl);
        var model = new ChatModel(modelName);
        var provider = new TornadoLLMProvider(api, model);
        builder.WithProvider(provider, options);
        builder.WithTokenCounter(new ApproximateTokenCounter());

        return builder;
    }

    public static AgentBuilder AddTornadoEmbeddingProvider(this AgentBuilder builder, string modelName, string apiKey, string? baseUrl = null)
    {
        var api = new TornadoApi(apiKey, baseUrl);
        var embedModel = new EmbeddingModel(modelName);
        var provider = new TornadoEmbeddingProvider(api, embedModel);
        builder.WithEmbeddingProvider(provider);

        return builder;
    }
}
