using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public static class AgentExtensions
{
    public static T? FindLayer<T>(this object? root) where T : class
    {
        for (var c = root; c != null; c = GetInner(c))
            if (c is T match) return match;
        return null;

        static object? GetInner(object obj) => obj switch
        {
            ContextLayer contextLayer => contextLayer.Inner,
            LLMLayer llmLayer => llmLayer.Inner,
            ToolboxLayer toolingLayer => toolingLayer.Inner,
            _ => null
        };
    }

    public static async Task<string?> GetFinalResponseAsync(
        this IAsyncEnumerable<IContentEvent> stream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        string? text = null;

        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
            if (evt is Text t) text = t.Value;

        return text;
    }

    public static IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        this IAgent agent,
        IContent input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(input);
        return agent.InvokeStreamingAsync([input], ct);
    }

    public static Agent UseLLM(this Agent agent, ILLM newLlm)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(newLlm);
        return agent.With(llm: newLlm);
    }

    public static Agent UseContext(this Agent agent, IContext newCtx)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(newCtx);
        return agent.With(context: newCtx);
    }

    public static Agent UseToolbox(this Agent agent, IToolbox newToolbox)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(newToolbox);
        return agent.With(toolbox: newToolbox);
    }

    public static Agent UseInstructions(this Agent agent, params IContent[] instructions)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(instructions);
        return agent.With(instructions: instructions);
    }
}
