namespace AgentCore.Memory;

/// <summary>
/// The ONE data type for all stored knowledge. 12 fields. One table.
/// Covers facts, experiences, beliefs, and consolidated observations.
/// Independent from CoreMemory - advanced AMFS decay, embeddings, semantic search.
/// </summary>
public sealed class MemoryEntry
{
    // ── Identity ───────────────────────────────────────────────────────────

    /// <summary>Unique identifier for this memory entry.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>The actual text content.</summary>
    public string Content { get; set; } = "";

    // ── Cognitive Memory Specific (2 fields) ──────────────────────────────────

    /// <summary>Dense vector for semantic similarity search. Null until embedded.</summary>
    public float[]? Embedding { get; set; }

    /// <summary>When this record was first created (UTC).</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // ── Classification (2 fields) ────────────────────────────────────────────

    /// <summary>Memory kind — drives decay multiplier and rendering order.</summary>
    public MemoryKind Kind { get; init; } = MemoryKind.Fact;

    /// <summary>
    /// Human-readable label or key. Required for Observations (gives the synthesis a title).
    /// Optional for facts/experiences. Maps to AMFS's "key" concept.
    /// </summary>
    public string Name { get; set; } = "";

    // ── AMFS Confidence Decay (3 fields) ─────────────────────────────────────

    /// <summary>
    /// Base confidence 0–1. Decays exponentially over time via the AMFS formula.
    /// Boosted by CommitOutcome(Success), reduced by CommitOutcome(Failure).
    /// </summary>
    public float Confidence { get; set; } = 1.0f;

    /// <summary>
    /// How many times this entry was retrieved. Increases effective half-life
    /// (frequently recalled memories decay slower — spaced repetition effect).
    /// </summary>
    public int RecallCount { get; set; }

    /// <summary>
    /// How many times this entry was involved in a successful outcome.
    /// OutcomeCount > 0 doubles the effective half-life (outcome-validated knowledge sticks).
    /// </summary>
    public int OutcomeCount { get; set; }

    // ── Consolidation / Provenance (2 fields) ────────────────────────────────

    /// <summary>
    /// Version number — incremented when an entry is updated in-place (upsert by Id).
    /// Enables optimistic concurrency and change tracking.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// IDs of source entries this was consolidated from (populated by DreamAsync/ReflectAsync).
    /// Empty for raw facts. Non-empty for Observations. Enables full provenance replay.
    /// </summary>
    public string[] SourceEntryIds { get; set; } = [];

    // ── Invalidation (1 field) — the "no hard-delete" mechanism ─────────────

    /// <summary>
    /// Soft-delete timestamp. When set, the entry is logically invalidated but physically preserved.
    /// Preserves full audit history. Use FindAsync(includeInvalidated: true) to see them.
    /// </summary>
    public DateTime? InvalidatedAt { get; set; }
}

/// <summary>
/// Memory classification enum. The kind drives the decay multiplier in the AMFS formula.
/// </summary>
public enum MemoryKind
{
    /// <summary>Verified, well-established knowledge. Decay multiplier: 1.0x (baseline).</summary>
    Fact,

    /// <summary>
    /// Personal episodic memory — what the agent experienced or did.
    /// Decay multiplier: 1.5x (experiences stick longer than plain facts).
    /// </summary>
    Experience,

    /// <summary>
    /// Unvalidated hypothesis or assumption. Decay multiplier: 0.5x (fades faster if not confirmed).
    /// Promotes to Fact via CommitOutcome(Success).
    /// </summary>
    Belief,

    /// <summary>
    /// Consolidated synthesis produced by DreamAsync or ReflectAsync.
    /// Enriched with Name, SourceEntryIds, Version. Decay multiplier: 1.0x like Facts.
    /// Rendered first during recall — highest informational density.
    /// </summary>
    Observation,

    /// <summary>
    /// Reusable learned workflow/procedure. Decay multiplier: 2.0x (skills persist).
    /// Content = JSON with {trigger, steps[{step,action,detail}]}.
    /// OutcomeCount tracks success. RecallCount tracks usage.
    /// </summary>
    Skill
}

/// <summary>
/// Outcome type for AMFS confidence adjustment via CommitOutcomeAsync.
/// </summary>
public enum OutcomeType
{
    /// <summary>Task succeeded — boost confidence by 1.1x.</summary>
    Success,

    /// <summary>Minor failure — reduce confidence by 0.95x.</summary>
    MinorFailure,

    /// <summary>Failure — reduce confidence by 0.8x.</summary>
    Failure,

    /// <summary>Critical failure — reduce confidence by 0.5x.</summary>
    CriticalFailure
}
