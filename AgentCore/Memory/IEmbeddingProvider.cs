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
