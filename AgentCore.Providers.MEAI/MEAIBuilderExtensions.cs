using System;
using AgentCore.LLM;
using AgentCore.LLM.MEAI;
using Microsoft.Extensions.AI;

namespace AgentCore;

/// <summary>
/// Builder extension methods for registering Microsoft.Extensions.AI provider.
/// </summary>
public static class MEAIBuilderExtensions
{
    /// <summary>
    /// Registers the Microsoft.Extensions.AI IChatClient provider on the Agent.Builder.
    /// </summary>
    public static Agent.Builder WithMEAI(
        this Agent.Builder builder,
        IChatClient client,
        LLMCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        return builder.WithLLM(new MEAILLM(client, capabilities));
    }

    /// <summary>
    /// Adapts a Microsoft.Extensions.AI AIFunction into an AgentCore Tool.
    /// </summary>
    public static AgentCore.Tools.Tool ToTool(this AIFunction aiFunction)
    {
        ArgumentNullException.ThrowIfNull(aiFunction);
        return new MEAIFunctionTool(aiFunction);
    }
}

