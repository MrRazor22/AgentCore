using LlmTornado;
using LlmTornado.Chat.Models;
using AgentCore.LLM;

namespace AgentCore.Providers.Tornado;

/// <summary>
/// Factory methods for creating Tornado-based LLM providers.
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
    public static ILLMProvider CreateLLMProvider(
        string apiKey, 
        IReadOnlyList<LLMMetadata> models,
        Uri? baseUrl = null)
    {
        var api = new TornadoApi(baseUrl, apiKey);
        return new TornadoLLMProvider(api, models);
    }
}


public static class TornadoAgentBuilderExtensions
{
    public static Agent.Builder AddTornado(
        this Agent.Builder builder, 
        string apiKey, 
        IReadOnlyList<LLMMetadata> models,
        Uri? baseUrl = null)
        => builder.WithProvider(TornadoProvider.CreateLLMProvider(apiKey, models, baseUrl));

}