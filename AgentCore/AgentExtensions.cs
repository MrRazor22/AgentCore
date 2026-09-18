using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.LLM;
using AgentCore.Tooling;

namespace AgentCore;

public static class AgentExtensions
{
    public static T? FindLayer<T>(this IContext context) where T : class
    {
        for (var c = context; c != null; c = (c as ContextLayer)?.Inner)
            if (c is T match) return match;
        return null;
    }

    public static T? FindLayer<T>(this ILLM llm) where T : class
    {
        for (var l = llm; l != null; l = (l as LLMLayer)?.Inner)
            if (l is T match) return match;
        return null;
    }

    public static T? FindLayer<T>(this IToolbox toolbox) where T : class
    {
        for (var t = toolbox; t != null; t = (t as ToolingLayer)?.Inner)
            if (t is T match) return match;
        return null;
    }
}
