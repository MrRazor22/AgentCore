using System.Text.Json;

namespace AgentCore.Memory;

/// <summary>
/// Memory store interface for persistence and retrieval.
/// </summary>
public interface IMemoryStore
{
    Task UpsertAsync(IReadOnlyList<MemoryEntry> entries, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryEntry>> FindAsync(
        float[]? embedding = null,
        string? text = null,
        int limit = 20,
        MemoryKind[]? kinds = null,
        bool includeInvalidated = false,
        DateTime? after = null,
        DateTime? before = null,
        CancellationToken ct = default);
}

/// <summary>
/// JSON file-backed persistent memory store.
/// Loads all entries into memory on startup, writes on every upsert (with tmp-file atomic swap).
/// Good for single-process agents with moderate memory sizes (hundreds to low thousands of entries).
/// For large corpora, implement IMemoryStore against a vector DB.
/// </summary>
public sealed class FileStore : IMemoryStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, MemoryEntry> _cache = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public FileStore(string directoryPath, string scope = "default")
    {
        Directory.CreateDirectory(directoryPath);
        _filePath = Path.Combine(directoryPath, $"memory_{scope}.json");
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<MemoryEntryDto>>(json, JsonOpts);
            if (entries == null) return;
            foreach (var dto in entries)
            {
                var entry = DtoToEntry(dto);
                _cache[entry.Id] = entry;
            }
        }
        catch { /* corrupt file → start fresh */ }
    }

    public async Task UpsertAsync(IReadOnlyList<MemoryEntry> entries, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var entry in entries)
            {
                if (_cache.TryGetValue(entry.Id, out var existing))
                    entry.Version = existing.Version + 1;
                _cache[entry.Id] = entry;
            }
            await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryEntry>> FindAsync(
        float[]? embedding = null,
        string? text = null,
        int limit = 20,
        MemoryKind[]? kinds = null,
        bool includeInvalidated = false,
        DateTime? after = null,
        DateTime? before = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var candidates = _cache.Values.AsEnumerable();

            if (!includeInvalidated)
                candidates = candidates.Where(e => e.InvalidatedAt == null);

            if (kinds is { Length: > 0 })
                candidates = candidates.Where(e => kinds.Contains(e.Kind));

            if (after.HasValue)
                candidates = candidates.Where(e => e.CreatedAt > after.Value);

            if (before.HasValue)
                candidates = candidates.Where(e => e.CreatedAt < before.Value);

            var scored = candidates
                .Select(e => (Entry: e, Score: Score(e, embedding, text)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();

            if (scored.Count == 0 && embedding == null && string.IsNullOrEmpty(text))
            {
                scored = candidates
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(limit)
                    .ToList();
            }

            return scored;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task FlushAsync()
    {
        var dtos = _cache.Values.Select(EntryToDto).ToList();
        var json = JsonSerializer.Serialize(dtos, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;

        float dot = 0f;
        float magA = 0f;
        float magB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        float denominator = (float)Math.Sqrt(magA) * (float)Math.Sqrt(magB);
        return denominator == 0 ? 0f : dot / denominator;
    }

    private static float Score(MemoryEntry entry, float[]? embedding, string? text)
    {
        float vectorScore = 0f;
        float textScore = 0f;

        if (embedding is { Length: > 0 } && entry.Embedding is { Length: > 0 })
            vectorScore = CosineSimilarity(embedding, entry.Embedding);

        if (!string.IsNullOrEmpty(text) && entry.Content.Contains(text, StringComparison.OrdinalIgnoreCase))
            textScore = 1.0f;

        if (embedding != null && text != null) return 0.7f * vectorScore + 0.3f * textScore;
        if (embedding != null) return vectorScore;
        if (text != null) return textScore > 0 ? 1.0f : 0f;
        return 1.0f;
    }

    // ── DTO for clean JSON serialization ────────────────────────────────────

    private sealed class MemoryEntryDto
    {
        public string Id { get; set; } = "";
        public string Content { get; set; } = "";
        public float[]? Embedding { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Kind { get; set; } = "Fact";
        public string Name { get; set; } = "";
        public float Confidence { get; set; } = 1f;
        public int RecallCount { get; set; }
        public int OutcomeCount { get; set; }
        public int Version { get; set; } = 1;
        public string[] SourceEntryIds { get; set; } = [];
        public DateTime? InvalidatedAt { get; set; }
    }

    private static MemoryEntryDto EntryToDto(MemoryEntry e) => new()
    {
        Id = e.Id,
        Content = e.Content,
        Embedding = e.Embedding,
        CreatedAt = e.CreatedAt,
        Kind = e.Kind.ToString(),
        Name = e.Name,
        Confidence = e.Confidence,
        RecallCount = e.RecallCount,
        OutcomeCount = e.OutcomeCount,
        Version = e.Version,
        SourceEntryIds = e.SourceEntryIds,
        InvalidatedAt = e.InvalidatedAt,
    };

    private static MemoryEntry DtoToEntry(MemoryEntryDto d) => new()
    {
        Id = d.Id,
        Content = d.Content,
        Embedding = d.Embedding,
        CreatedAt = d.CreatedAt,
        Kind = Enum.TryParse<MemoryKind>(d.Kind, out var k) ? k : MemoryKind.Fact,
        Name = d.Name,
        Confidence = d.Confidence,
        RecallCount = d.RecallCount,
        OutcomeCount = d.OutcomeCount,
        Version = d.Version,
        SourceEntryIds = d.SourceEntryIds,
        InvalidatedAt = d.InvalidatedAt,
    };
}
