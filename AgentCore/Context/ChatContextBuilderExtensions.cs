using Microsoft.Extensions.Logging;
using AgentCore.LLM;

namespace AgentCore.Context;

public static class ChatContextBuilderExtensions
{
    public static Agent.Builder WithChatContext(
        this Agent.Builder builder, 
        int contextWindow = 50000, 
        int? reserveTokens = null,
        int? maxSingleMessageTokens = null,
        ICompactor? compactor = null,
        ILLM? summarizer = null)
    {
        return builder.WithContext(lf => new ChatContext(
            contextWindow: contextWindow,
            reserveTokens: reserveTokens,
            maxSingleMessageTokens: maxSingleMessageTokens,
            compactor: compactor,
            summarizer: summarizer,
            logger: lf.CreateLogger<ChatContext>()
        ));
    }
}
