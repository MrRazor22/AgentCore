using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Embedding.Models;
using AgentCore.LLM;
using AgentCore.Memory;

namespace AgentCore.Providers.Tornado;

/// <summary>
/// Factory methods for creating Tornado-based LLM and embedding providers.
/// Use these to create separate providers for different components (e.g., agent vs memory).
/// </summary>
public static class TornadoProvider
{
    /// <summary>
    /// Creates an LLM provider using Tornado API.
    /// </summary>
    /// <param name="apiKey">API key for the LLM service</param>
    /// <param name="modelName">Model name (e.g., "gpt-4o", "gpt-4o-mini")</param>
    /// <param name="baseUrl">Optional base URL for the API endpoint</param>
    /// <returns>An ILLMProvider instance</returns>
    public static ILLMProvider CreateLLMProvider(string apiKey, string modelName, Uri? baseUrl = null)
    {
        var api = new TornadoApi(baseUrl, apiKey);
        var model = new ChatModel(modelName);
        return new TornadoLLMProvider(api, model);
    }
    
    /// <summary>
    /// Creates an embedding provider using Tornado API.
    /// </summary>
    /// <param name="apiKey">API key for the embedding service</param>
    /// <param name="modelName">Embedding model name (e.g., "text-embedding-3-small")</param>
    /// <param name="baseUrl">Optional base URL for the API endpoint</param>
    /// <returns>An IEmbeddingProvider instance</returns>
    public static IEmbeddingProvider CreateEmbeddingProvider(string apiKey, string modelName, Uri? baseUrl = null)
    {
        var api = new TornadoApi(baseUrl, apiKey);
        var model = new EmbeddingModel(modelName);
        return new TornadoEmbeddingProvider(api, model);
    }
}
