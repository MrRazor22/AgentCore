namespace AgentCore.LLM;

public static class LLMExtensions
{
    public static ILLM Attach(this ILLM llm, ILLM inner)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(inner);
        if (llm is ILayer<ILLM> layer) layer.Attach(inner);
        return llm;
    }

    public static ILLM AddLayer(this ILLM llm, ILLM layer)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(layer);
        if (llm is ILayer<ILLM> head && layer is ILayer<ILLM> next)
        {
            next.Attach(head.Inner);
            head.Attach(layer);
            return llm;
        }
        if (layer is ILayer<ILLM> l) l.Attach(llm);
        return layer;
    }

    public static ILLM RemoveLayer<T>(this ILLM llm) where T : class
    {
        if (llm is T && llm is ILayer<ILLM> self) return self.Inner.RemoveLayer<T>();
        if (llm is ILayer<ILLM> head)
        {
            var newInner = head.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, head.Inner)) head.Attach(newInner);
        }
        return llm;
    }

    public static TL? FindLayer<TL>(this ILLM root) where TL : class
    {
        for (var c = root; c != null; c = (c as ILayer<ILLM>)?.Inner)
            if (c is TL match) return match;
        return null;
    }
}
