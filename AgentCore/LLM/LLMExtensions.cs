namespace AgentCore.LLM;

public static class LLMExtensions
{
    public static ILLM Attach(this ILLM llm, ILLM inner)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(inner);
        if (llm is LLMLayer ml) ml.Attach(inner);
        return llm;
    }

    public static ILLM AddLayer(this ILLM llm, LLMLayer layer)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(layer);
        if (llm is LLMLayer ml) return ml.AddLayer(layer);
        layer.Attach(llm);
        return layer;
    }

    public static ILLM RemoveLayer<T>(this ILLM llm) where T : class
        => llm is LLMLayer ml ? ml.RemoveLayer<T>() : llm;
}
