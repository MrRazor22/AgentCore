using LlmTornado;
using LlmTornado.Chat.Models;
using AgentCore.LLM;

namespace AgentCore.LLM.Tornado;

/// <summary>
/// Factory methods for creating Tornado-based LLM providers.
/// </summary>
public static class TornadoLLMFactory
{
    /// <summary>
    /// Creates an LLM provider using Tornado API.
    /// </summary>
    public static ILLM CreateLLMProvider(
        string apiKey, 
        string modelName,
        LLMCapabilities? capabilities = null,
        Uri? baseUrl = null)
    {
        var api = new TornadoApi(baseUrl, apiKey);
        return new TornadoLLM(api, modelName, capabilities ?? new LLMCapabilities { ContextWindow = 128000 });
    }
}

public static class TornadoAgentBuilderExtensions
{
    public static Agent.Builder AddTornado(
        this Agent.Builder builder, 
        string apiKey, 
        string modelName,
        LLMCapabilities? capabilities = null,
        Uri? baseUrl = null)
        => builder.WithLLM(TornadoLLMFactory.CreateLLMProvider(apiKey, modelName, capabilities, baseUrl));
}