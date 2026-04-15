using System.Collections.Concurrent;

namespace AgentCore.Memory;

/// <summary>
/// Volatile in-memory store. Entries are lost when the process exits.
/// Good for tests, prototyping, and short-lived agent sessions.
/// Brute-force cosine similarity (O(n)) — use a real vector DB for large corpora.
/// </summary>
public sealed class InMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<string, MemoryEntry> _store = new();

    public Task UpsertAsync(IReadOnlyList<MemoryEntry> entries, CancellationToken ct = default)
    {
        foreach (var entry in entries)
        {
            _store.AddOrUpdate(
                entry.Id,
                _ => entry,
                (_, existing) =>
                {
                    // Preserve Id and CreatedAt on update, bump version
                    entry.Version = existing.Version + 1;
                    return entry;
                });
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> FindAsync(
        float[]? embedding = null,
        string? text = null,
        int limit = 20,
        MemoryKind[]? kinds = null,
        bool includeInvalidated = false,
        DateTime? after = null,
        DateTime? before = null,
        CancellationToken ct = default)
    {
        var candidates = _store.Values.AsEnumerable();

        if (!includeInvalidated)
            candidates = candidates.Where(e => e.InvalidatedAt == null);

        if (kinds is { Length: > 0 })
            candidates = candidates.Where(e => kinds.Contains(e.Kind));

        if (after.HasValue)
            candidates = candidates.Where(e => e.CreatedAt > after.Value);

        if (before.HasValue)
            candidates = candidates.Where(e => e.CreatedAt < before.Value);

        // Scoring: combine embedding cosine + text keyword match
        var scored = candidates
            .Select(e => (Entry: e, Score: Score(e, embedding, text)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Entry)
            .ToList();

        // If no scoring signal, fall back to most-recent
        if (scored.Count == 0 && embedding == null && string.IsNullOrEmpty(text))
        {
            scored = candidates
                .OrderByDescending(e => e.CreatedAt)
                .Take(limit)
                .ToList();
        }

        return Task.FromResult<IReadOnlyList<MemoryEntry>>(scored);
    }

    private static float Score(MemoryEntry entry, float[]? embedding, string? text)
    {
        float vectorScore = 0f;
        float textScore = 0f;

        if (embedding is { Length: > 0 } && entry.Embedding is { Length: > 0 })
            vectorScore = CosineSimilarity(embedding, entry.Embedding);

        if (!string.IsNullOrEmpty(text) && entry.Content.Contains(text, StringComparison.OrdinalIgnoreCase))
            textScore = 1.0f;

        if (embedding != null && text != null)
            return 0.7f * vectorScore + 0.3f * textScore;
        if (embedding != null)
            return vectorScore;
        if (text != null)
            return textScore > 0 ? 1.0f : 0f;

        return 1.0f; // no filter = include everything
    }

    internal static float CosineSimilarity(float[] a, float[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < float.Epsilon ? 0f : dot / denom;
    }
}
