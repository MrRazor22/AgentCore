namespace AgentCore.LLM;

public static class LLMExtensions
{
    public static ILLM AddLayer(this ILLM llm, LLMLayer layer)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(llm);
        return layer;
    }

    public static ILLM RemoveLayer<T>(this ILLM llm) where T : class
    {
        if (llm is T layer && layer is LLMLayer ll)
            return ll.Inner.RemoveLayer<T>();

        if (llm is LLMLayer parent)
        {
            var newInner = parent.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, parent.Inner))
                parent.Attach(newInner);
        }
        return llm;
    }
}
