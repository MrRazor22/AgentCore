using LlmTornado;
using LlmTornado.Models;
using LlmTornado.Chat.Models;
using AgentCore.LLM;
using AgentCore.Tokens;

namespace AgentCore.Providers.Tornado;

public static class TornadoAgentBuilderExtensions
{
    public static AgentBuilder AddTornado(this AgentBuilder builder, TornadoApi api, ChatModel model, LLMOptions? options = null)
    {
        var provider = new TornadoLLMProvider(api, model);
        builder.WithProvider(provider, options);
        builder.WithTokenCounter(new ApproximateTokenCounter());
        return builder;
    }
}
