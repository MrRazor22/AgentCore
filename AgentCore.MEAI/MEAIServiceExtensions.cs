using AgentCore.LLM;
using AgentCore.Tokens;
using Microsoft.Extensions.AI;

namespace AgentCore.Providers.MEAI;

public static class MEAIServiceExtensions
{
    /// <summary>
    /// Adds a Microsoft.Extensions.AI IChatClient as the LLM provider for the agent.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="client">The IChatClient instance to wrap.</param>
    /// <returns>The builder instance.</returns>
    public static AgentBuilder AddMEAI(this AgentBuilder builder, IChatClient client)
    {
        var provider = new MEAILLMClient(client);
        builder.WithProvider(provider);
        
        // ApproximateTokenCounter is the default universal counter
        // and it will calibrate itself from the UsageContent returned by MEAI.
        builder.WithTokenCounter(new ApproximateTokenCounter());
        
        return builder;
    }
}
