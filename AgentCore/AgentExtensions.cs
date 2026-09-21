using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public static class AgentExtensions
{
    public static Agent With(
        this IAgent agent,
        ILLM? llm = null,
        IToolbox? toolbox = null,
        IContext? context = null,
        IReadOnlyList<IContent>? instructions = null,
        int? maxIterations = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new(
            llm ?? agent.LLM,
            toolbox ?? agent.Toolbox,
            context ?? agent.Context,
            instructions ?? agent.Instructions,
            maxIterations ?? (agent as Agent)?.MaxIterations ?? 20);
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
}

public interface ILayer<T>
{
    T Inner { get; }
    void Attach(T inner);
}
