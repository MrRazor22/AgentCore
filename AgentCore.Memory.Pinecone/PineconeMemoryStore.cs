using AgentCore.Memory;
using Microsoft.Extensions.Logging;
using Pinecone;
using System.Text.Json;

namespace AgentCore.Memory.Pinecone;

/// <summary>
/// Pinecone-based implementation of IMemoryStore for semantic memory.
/// Stores MemoryEntry records as vectors with metadata in Pinecone.
/// Supports semantic search via cosine similarity and text filtering.
/// </summary>
public sealed class PineconeMemoryStore : IMemoryStore
{
    private readonly PineconeClient _client;
    private readonly string _indexName;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<PineconeMemoryStore> _logger;

    public PineconeMemoryStore(
        string apiKey,
        string indexName,
        IEmbeddingProvider? embeddingProvider = null,
        ILogger<PineconeMemoryStore>? logger = null)
    {
        _client = new PineconeClient(apiKey);
        _indexName = indexName;
        _embeddingProvider = embeddingProvider ?? NullEmbeddingProvider.Instance;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PineconeMemoryStore>.Instance;
    }

    private dynamic GetIndex()
    {
        return _client.Index(_indexName);
    }

    public async Task UpsertAsync(IReadOnlyList<MemoryEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return;

        var index = GetIndex();
        var vectors = new List<Vector>();

        foreach (var entry in entries)
        {
            // Generate embedding if not already present
            float[] embedding = entry.Embedding ?? await _embeddingProvider.EmbedAsync(entry.Content, ct);

            // Convert MemoryEntry to metadata
            var metadata = new Metadata
            {
                ["content"] = entry.Content,
                ["kind"] = entry.Kind.ToString(),
                ["confidence"] = entry.Confidence,
                ["recallCount"] = entry.RecallCount,
                ["outcomeCount"] = entry.OutcomeCount,
                ["version"] = entry.Version,
                ["createdAt"] = entry.CreatedAt.ToString("O"),
                ["name"] = entry.Name,
                ["sourceEntryIds"] = JsonSerializer.Serialize(entry.SourceEntryIds)
            };

            if (entry.InvalidatedAt.HasValue)
            {
                metadata["invalidatedAt"] = entry.InvalidatedAt.Value.ToString("O");
            }

            vectors.Add(new Vector
            {
                Id = entry.Id,
                Values = embedding,
                Metadata = metadata
            });
        }

        await index.UpsertAsync(new UpsertRequest { Vectors = vectors }, cancellationToken: ct);
        _logger.LogInformation("Upserted {Count} memory entries to Pinecone index {Index}", entries.Count, _indexName);
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
        var index = GetIndex();
        
        // Build filter
        var filter = new Metadata();

        if (kinds != null && kinds.Length > 0)
        {
            filter["kind"] = new Metadata
            {
                ["$in"] = kinds.Select(k => k.ToString()).ToArray()
            };
        }

        if (!includeInvalidated)
        {
            filter["invalidatedAt"] = new Metadata
            {
                ["$is_null"] = true
            };
        }

        if (after.HasValue)
        {
            filter["createdAt"] = new Metadata
            {
                ["$gte"] = after.Value.ToString("O")
            };
        }

        if (before.HasValue)
        {
            filter["createdAt"] = new Metadata
            {
                ["$lte"] = before.Value.ToString("O")
            };
        }

        QueryResponse? queryResponse = null;
        
        if (embedding != null && embedding.Length > 0)
        {
            // Semantic search
            queryResponse = await index.QueryAsync(
                new QueryRequest
                {
                    Vector = embedding,
                    TopK = (uint)limit,
                    Filter = filter.Count > 0 ? filter : null,
                    IncludeMetadata = true,
                    IncludeValues = true
                },
                cancellationToken: ct);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            // Text-only search (generate embedding for query)
            var queryEmbedding = await _embeddingProvider.EmbedAsync(text, ct);
            if (queryEmbedding.Length > 0)
            {
                queryResponse = await index.QueryAsync(
                    new QueryRequest
                    {
                        Vector = queryEmbedding,
                        TopK = (uint)limit,
                        Filter = filter.Count > 0 ? filter : null,
                        IncludeMetadata = true,
                        IncludeValues = true
                    },
                    cancellationToken: ct);
            }
        }
        else
        {
            // No search criteria, return recent entries - need a placeholder vector
            // Use zero vector of appropriate dimension
            var zeroVector = new float[1536]; // Default dimension, should be configured
            queryResponse = await index.QueryAsync(
                new QueryRequest
                {
                    Vector = zeroVector,
                    TopK = (uint)limit,
                    Filter = filter.Count > 0 ? filter : null,
                    IncludeMetadata = true,
                    IncludeValues = true
                },
                cancellationToken: ct);
        }

        // Convert back to MemoryEntry
        var entries = new List<MemoryEntry>();
        if (queryResponse?.Matches != null)
        {
            foreach (var match in queryResponse.Matches)
            {
                if (match.Metadata == null) continue;

                var entry = new MemoryEntry
                {
                    Id = match.Id ?? "",
                    Content = GetStringMetadata(match.Metadata, "content"),
                    Embedding = match.Values?.ToArray(),
                    Kind = ParseEnum<MemoryKind>(GetStringMetadata(match.Metadata, "kind")),
                    Confidence = GetFloatMetadata(match.Metadata, "confidence"),
                    RecallCount = GetIntMetadata(match.Metadata, "recallCount"),
                    OutcomeCount = GetIntMetadata(match.Metadata, "outcomeCount"),
                    Version = GetIntMetadata(match.Metadata, "version"),
                    CreatedAt = ParseDateTime(GetStringMetadata(match.Metadata, "createdAt")),
                    Name = GetStringMetadata(match.Metadata, "name"),
                    SourceEntryIds = ParseJsonArray<string>(GetStringMetadata(match.Metadata, "sourceEntryIds"))
                };

                var invalidatedAt = GetStringMetadata(match.Metadata, "invalidatedAt");
                if (!string.IsNullOrEmpty(invalidatedAt))
                {
                    entry.InvalidatedAt = ParseDateTime(invalidatedAt);
                }

                entries.Add(entry);
            }
        }

        _logger.LogDebug("Found {Count} memory entries from Pinecone", entries.Count);
        return entries;
    }

    private static string GetStringMetadata(Metadata metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value) && value != null)
        {
            return value.ToString() ?? "";
        }
        return "";
    }

    private static float GetFloatMetadata(Metadata metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value) && value != null)
        {
            if (float.TryParse(value.ToString(), out var result))
                return result;
        }
        return 0f;
    }

    private static int GetIntMetadata(Metadata metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value) && value != null)
        {
            if (int.TryParse(value.ToString(), out var result))
                return result;
        }
        return 0;
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, true, out var result))
            return result;
        return default;
    }

    private static DateTime ParseDateTime(string value)
    {
        if (DateTime.TryParse(value, out var result))
            return result;
        return DateTime.UtcNow;
    }

    private static T[] ParseJsonArray<T>(string json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<T[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
