using AgentCore.LLM.Chat;

namespace AgentCore;

public static class AgentExtensions
{
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
