namespace AgentCore.LLM;

public static class LLMLayerExtensions
{
    public static Agent AddLayer(this Agent agent, LLMLayer layer)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Attach(agent.LLM);
        return agent.With(llm: layer);
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

    public static Agent RemoveLayer<T>(this Agent agent) where T : LLMLayer
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.With(llm: agent.LLM.RemoveLayer<T>());
    }
}
