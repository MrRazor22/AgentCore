using LlmTornado;
using LlmTornado.Chat.Models;
using AgentCore.LLM;

namespace AgentCore.Providers.Tornado;

public static class TornadoProvider
{
    public static ILLMProvider CreateLLMProvider(string apiKey, string modelName, Uri? baseUrl = null)
    {
        var api = new TornadoApi(baseUrl, apiKey);
        var model = new ChatModel(modelName);
        return new TornadoLLMProvider(api, model);
    }
}
