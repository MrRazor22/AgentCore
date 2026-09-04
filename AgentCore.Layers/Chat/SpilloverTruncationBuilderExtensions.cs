namespace AgentCore.Layers.Chat;

public static class SpilloverTruncationBuilderExtensions
{
    public static Agent.Builder WithSpilloverTruncation(
        this Agent.Builder builder,
        int maxTokens = 10_000,
        string? sessionId = null,
        string? storageDir = null,
        bool autoDeleteOnDispose = true,
        Func<string, int, string>? noticeFormatter = null)
    {
        return builder.AddContextLayer(new SpilloverTruncationLayer(
            maxTokens: maxTokens,
            sessionId: sessionId,
            storageDir: storageDir,
            autoDeleteOnDispose: autoDeleteOnDispose,
            noticeFormatter: noticeFormatter));
    }
}
