using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public static class ChatContextBuilderExtensions
{
    public static Agent.Builder WithChatContext(
        this Agent.Builder builder,
        int contextWindow = 50000,
        int? reserveTokens = null,
        int? maxSingleMessageTokens = null,
        ICompactor? compactor = null,
        ITokenizer? counter = null,
        ITruncator? truncator = null)
    {
        return builder.WithContext(lf => new ChatContext(
            contextWindow: contextWindow,
            reserveTokens: reserveTokens,
            maxSingleMessageTokens: maxSingleMessageTokens,
            compactor: compactor,
            counter: counter,
            truncator: truncator, 
            logger: lf.CreateLogger<ChatContext>()
        ));
    }
}