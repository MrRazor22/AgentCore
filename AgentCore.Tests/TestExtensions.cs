using System.Text;
using System.Text.Json;
using AgentCore.LLM.Chat;

namespace AgentCore.Tests;

internal static class TestExtensions
{
    public static IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        this IAgent agent,
        IContent input,
        CancellationToken ct = default) => agent.InvokeStreamingAsync([input], ct);

    public static async Task<string?> InvokeAsync(
        this IAgent agent,
        IContent input,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        bool hasDeltas = false;

        await foreach (var evt in agent.InvokeStreamingAsync([input], ct))
        {
            if (evt is TextDelta td) { sb.Append(td.Text); hasDeltas = true; }
            else if (evt is Text t && !hasDeltas) sb.Append(t.Value);
        }

        return sb.ToString();
    }

    public static async Task<T?> InvokeAsync<T>(
        this IAgent agent,
        IContent input,
        CancellationToken ct = default)
    {
        var text = await agent.InvokeAsync(input, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text);
    }
}
