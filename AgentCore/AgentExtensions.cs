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
            ToolingLayer toolingLayer => toolingLayer.Inner,
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
        string prompt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(prompt);
        return agent.InvokeStreamingAsync([new Text(prompt)], ct);
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
}
