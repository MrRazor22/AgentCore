using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;

/// <summary>
/// Represents a compacted summary checkpoint in conversation history.
/// Derived from <see cref="Text"/> so it functions seamlessly as plain text for models and downstream pipelines,
/// while allowing persistence and context layers to detect compaction boundaries.
/// </summary>
public record CompactedSummary(string Value) : Text(Value)
{
    public override string ToString() => Value;
}
