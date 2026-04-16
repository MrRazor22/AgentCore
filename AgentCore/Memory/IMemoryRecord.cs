namespace AgentCore.Memory;

/// <summary>
/// Universal contract for knowledge that can be rendered with a Header and Body.
/// Shared base for both CoreMemory (in-context) and Cognitive Memory (retrieved).
/// </summary>
public interface IMemoryRecord
{
    /// <summary>Header/Label (e.g., "scratchpad", "memory_123")</summary>
    string Id { get; }
    
    /// <summary>The actual text body</summary>
    string Content { get; }
}
