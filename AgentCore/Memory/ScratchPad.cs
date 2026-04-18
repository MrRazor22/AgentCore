using AgentCore.Conversation;

namespace AgentCore;

/// <summary>
/// A named, bounded, optionally agent-editable chunk of text injected into the prompt.
/// Used for both static instructions and agent-writable working memory.
/// Simple - no decay, always injected if provided.
/// </summary>
public sealed class ScratchPad
{ 
    public Role Role { get; }

    private string _value;

    /// <summary>Unique label for this item (e.g., "instructions", "scratchpad", "persona").</summary>
    public string Label { get; } 

    /// <summary>Max characters allowed. 0 = unlimited.</summary>
    public int Limit { get; }

    /// <summary>If false, the agent can update this item via tools.</summary>
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

    public ScratchPad(string label, string value, Role role = Role.System, int limit = 0, bool readOnly = true)
    {
        Label = label;
        Role = role;
        Limit = limit;
        ReadOnly = readOnly;
        _value = limit > 0 && value.Length > limit ? value[..limit] : value;
    }

    /// <summary>
    /// Renders the item as a XML-style string for the LLM.
    /// </summary>
    public string ToLlmString()
        => !string.IsNullOrEmpty(Value) ? $"<{Label}>\n{Value}\n</{Label}>" : string.Empty;
}
