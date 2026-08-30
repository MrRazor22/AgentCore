using AgentCore.LLM;
using AgentCore.LLM.Tornado;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentCore;

/// <summary>
/// Builder extension methods for registering LLMTornado provider.
/// </summary>
public static class TornadoBuilderExtensions
{
    /// <summary>
    /// Registers the LLMTornado provider on the Agent.Builder.
    /// </summary>
    public static Agent.Builder WithTornado(
        this Agent.Builder builder,
        TornadoApi api,
        ChatModel model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(model);

        return builder.WithLLM(_ => new TornadoLLM(api, model));
    }

    /// <summary>
    /// Registers the LLMTornado provider with an API key and provider on the Agent.Builder.
    /// </summary>
    public static Agent.Builder WithTornado(
        this Agent.Builder builder,
        string apiKey,
        LLmProviders provider,
        ChatModel model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(model);

        var api = new TornadoApi(new List<ProviderAuthentication>
        {
            new ProviderAuthentication(provider, apiKey)
        });

        return builder.WithTornado(api, model);
    }
}
