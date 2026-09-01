using AgentCore.LLM.MEAI;
using Microsoft.Extensions.AI;

namespace AgentCore;

/// <summary>
/// Builder extension methods for registering Microsoft.Extensions.AI IChatClient.
/// </summary>
public static class MEAIBuilderExtensions
{
    /// <summary>
    /// Registers the Microsoft.Extensions.AI IChatClient on the Agent.Builder.
    /// </summary>
    public static Agent.Builder WithMEAI(this Agent.Builder builder, IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);

        return builder.WithLLM(_ => new MEAILLM(chatClient));
    }
}
