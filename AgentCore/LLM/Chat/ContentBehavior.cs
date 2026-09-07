namespace AgentCore.LLM.Chat;

public interface IContentBehavior<T> where T : IContent
{
    int EstimateTokens(T content);
    T Truncate(T content, int maxTokens, string? notice = null);
}

public sealed class ContentBehaviors
{
    private sealed record BehaviorEntry(
        Func<IContent, int> Estimate,
        Func<IContent, int, string?, IContent> Truncate);

    private readonly Dictionary<Type, BehaviorEntry> _behaviors = new();

    public ContentBehaviors With<T>(IContentBehavior<T> behavior) where T : IContent
    {
        _behaviors[typeof(T)] = new BehaviorEntry(
            content => behavior.EstimateTokens((T)content),
            (content, maxTokens, notice) => behavior.Truncate((T)content, maxTokens, notice));
        return this;
    }

    public int Estimate(IContent content)
    {
        if (_behaviors.TryGetValue(content.GetType(), out var entry))
            return entry.Estimate(content);

        return content.EstimateTokens();
    }

    public IContent Truncate(IContent content, int maxTokens, string? notice = null)
    {
        if (_behaviors.TryGetValue(content.GetType(), out var entry))
            return entry.Truncate(content, maxTokens, notice);

        return content.Truncate(maxTokens, notice);
    }
}
