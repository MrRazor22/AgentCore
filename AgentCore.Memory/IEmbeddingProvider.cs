namespace AgentCore.Memory;

/// <summary>
/// Converts text to dense embedding vectors for semantic similarity search.
/// No Dimensions property — store implementations infer dimensions from the first vector's Length.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Embed a single piece of text into a dense float vector.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// No-op embedding provider. Returns empty arrays.
/// Use when no embedding model is available — text-only search still works via FindAsync(text:).
/// </summary>
public sealed class NullEmbeddingProvider : IEmbeddingProvider
{
    public static readonly NullEmbeddingProvider Instance = new();
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<float>());
}
