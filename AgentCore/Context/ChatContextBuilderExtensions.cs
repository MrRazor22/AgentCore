using Microsoft.Extensions.Logging;
using AgentCore.LLM;

namespace AgentCore.Context;

public static class ChatContextBuilderExtensions
{
    public static Agent.Builder WithChatContext(
        this Agent.Builder builder, 
        int contextWindow = 50000, 
        int? reserveTokens = 2000,
        ILLM? summarizer = null)
    {
        return builder.WithContext(lf => new ChatContext(
            contextWindow: contextWindow,
            reserveTokens: reserveTokens,
            summarizer: summarizer,
            logger: lf.CreateLogger<ChatContext>()
        ));
    }
}
