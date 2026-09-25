namespace AgentCore.LLM;

public static class LLMExtensions
{
    public static TTarget? Find<TTarget>(this ILLM? llm, Action<TTarget>? configure = null) where TTarget : class
    {
        for (var curr = llm; curr != null; curr = curr is ILayer<ILLM> l ? l.Inner : null)
            if (curr is TTarget match) { configure?.Invoke(match); return match; }
        return null;
    }
}
