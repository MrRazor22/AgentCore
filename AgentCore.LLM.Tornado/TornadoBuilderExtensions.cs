using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Tornado;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentCore.LLM.Tornado;

/// <summary>
/// Builder extension methods for registering LLMTornado provider.
/// </summary>
public static class TornadoBuilderExtensions
{
    /// <summary>
    /// Registers the LLMTornado provider on the LLMBuilder.
    /// </summary>
    public static LLMBuilder WithTornado(
        this LLMBuilder builder,
        TornadoApi api,
        ChatModel model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(model);

        return builder.Use(_ => new TornadoLLM(api, model));
    }

    /// <summary>
    /// Registers the LLMTornado provider using an API key, model name, and optional custom endpoint on LLMBuilder.
    /// </summary>
    public static LLMBuilder WithTornado(
        this LLMBuilder builder,
        string apiKey,
        string model,
        string? baseUrl = null,
        LLmProviders provider = LLmProviders.Custom)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(model);

        TornadoApi api = !string.IsNullOrWhiteSpace(baseUrl)
            ? new TornadoApi(new Uri(baseUrl), apiKey, provider)
            : new TornadoApi(provider, apiKey);

        var chatModel = new ChatModel(model, provider);
        return builder.WithTornado(api, chatModel);
    }

    /// <summary>
    /// Registers the LLMTornado provider on the AgentBuilder via UseLLM.
    /// </summary>
    public static AgentBuilder WithTornado(
        this AgentBuilder builder,
        TornadoApi api,
        ChatModel model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithTornado(api, model));
    }

    /// <summary>
    /// Registers the LLMTornado provider using an API key, model name, and optional custom endpoint on AgentBuilder via UseLLM.
    /// </summary>
    public static AgentBuilder WithTornado(
        this AgentBuilder builder,
        string apiKey,
        string model,
        string? baseUrl = null,
        LLmProviders provider = LLmProviders.Custom)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithTornado(apiKey, model, baseUrl, provider));
    }
}
