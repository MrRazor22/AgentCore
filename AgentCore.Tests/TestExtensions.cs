using System.Text.Json;

namespace AgentCore.Tests;

internal static class TestExtensions
{

    public static async Task<T?> WithResponse<T>(
        this IAgent agent,
        IContent input,
        CancellationToken ct = default)
    {
        var text = await agent.InvokeStreamingAsync([input], ct).GetFinalResponseAsync(ct);
        if (typeof(T) == typeof(string)) return (T?)(object?)text;
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text);
    }
}
