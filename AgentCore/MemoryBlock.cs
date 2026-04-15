using AgentCore.Conversation;

namespace AgentCore;

/// <summary>
/// A named, bounded, optionally agent-editable chunk of text injected into the prompt.
/// Inspired by Letta's Core Memory blocks.
/// </summary>
public sealed class MemoryBlock
{
    private string _value;

    /// <summary>Unique label for this block (e.g., "instructions", "scratchpad", "persona").</summary>
    public string Label { get; }

    /// <summary>Which role this block should appear as (System or User).</summary>
    public Role Role { get; }

    /// <summary>Max characters allowed. 0 = unlimited.</summary>
    public int Limit { get; }

    /// <summary>If false, the agent can update this block via tools.</summary>
    public bool ReadOnly { get; }

    /// <summary>
    /// The actual text content. Truncated to Limit if necessary.
    /// </summary>
    public string Value
    {
        get => _value;
        set => _value = Limit > 0 && value.Length > Limit 
            ? value[..Limit] 
            : value;
    }

    public MemoryBlock(string label, string value, Role role = Role.System, int limit = 0, bool readOnly = true)
    {
        Label = label;
        Role = role;
        Limit = limit;
        ReadOnly = readOnly;
        _value = limit > 0 && value.Length > limit ? value[..limit] : value;
    }

    /// <summary>
    /// Renders the block as a XML-style string for the LLM.
    /// </summary>
    public string ToLlmString() 
        => !string.IsNullOrEmpty(Value) ? $"<{Label}>\n{Value}\n</{Label}>" : string.Empty;
}
