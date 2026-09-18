using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Tornado;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentCore.LLM.Tornado;

public static class TornadoExtensions
{
    public static Agent WithTornado(
        this Agent agent,
        TornadoApi api,
        ChatModel model)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(model);
        return agent.WithLLM(new TornadoLLM(api, model));
    }

    public static Agent WithTornado(
        this Agent agent,
        string apiKey,
        string model,
        string? baseUrl = null,
        LLmProviders provider = LLmProviders.Custom)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(model);

        TornadoApi api;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var cleanUrl = baseUrl.TrimEnd('/');
            if (cleanUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                cleanUrl = cleanUrl[..^3];
            cleanUrl += "/";
            api = new TornadoApi(new Uri(cleanUrl), apiKey, provider);
        }
        else
        {
            api = new TornadoApi(provider, apiKey);
        }

        var chatModel = new ChatModel(model, provider);
        return agent.WithLLM(new TornadoLLM(api, chatModel));
    }
}
