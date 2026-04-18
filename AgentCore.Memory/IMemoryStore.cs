namespace AgentCore.Memory;

/// <summary>
/// Pluggable persistence for memory entries. Two methods — upsert and find.
/// Framework ships: InMemoryStore, FileStore.
/// Users implement: SqliteStore, PineconeStore, QdrantStore, etc.
/// </summary>
public interface IMemoryStore
{
    /// <summary>
    /// Insert or update entries by Id. Called during Retain, Dream, and CommitOutcome.
    /// Implementations must handle both insert (new Id) and update (existing Id) atomically.
    /// </summary>
    Task UpsertAsync(IReadOnlyList<MemoryEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// Multi-modal search across stored entries. All parameters are optional and combine with AND logic.
    /// Returns entries ordered by relevance score (highest first).
    /// </summary>
    /// <param name="embedding">If set, ranks by cosine similarity to this vector.</param>
    /// <param name="text">If set, filters/scores entries whose Content contains this text (BM25-style).</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="kinds">If set, filters to only these memory kinds.</param>
    /// <param name="includeInvalidated">If true, includes soft-deleted entries (audit mode).</param>
    /// <param name="after">If set, only returns entries created after this UTC timestamp.</param>
    /// <param name="before">If set, only returns entries created before this UTC timestamp.</param>
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
