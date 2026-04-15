namespace AgentCore.Memory;

/// <summary>
/// Core abstraction for all stored knowledge — the IContent of the memory layer.
/// Enables generic embedding and rendering across all MemoryEntry types.
/// </summary>
public interface IMemoryRecord
{
    /// <summary>Unique identifier for this memory record.</summary>
    string Id { get; }

    /// <summary>The text content — embeddable, searchable, renderable.</summary>
    string Content { get; }

    /// <summary>Dense vector for semantic similarity search. Null until embedded.</summary>
    float[]? Embedding { get; set; }

    /// <summary>When this record was first created (UTC).</summary>
    DateTime CreatedAt { get; }
}
