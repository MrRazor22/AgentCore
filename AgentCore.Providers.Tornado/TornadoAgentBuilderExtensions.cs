using LlmTornado;
using LlmTornado.Models;
using LlmTornado.Chat.Models;
using AgentCore.LLM;
using AgentCore.Tokens;

namespace AgentCore.Providers.Tornado;

public static class TornadoAgentBuilderExtensions
{
    public static AgentBuilder AddTornado(this AgentBuilder builder, TornadoApi api, string modelName, LLMOptions? options = null)
    => builder.WithProvider(new TornadoLLMProvider(
            api,
            new ChatModel(modelName)), 
            options); 
}
