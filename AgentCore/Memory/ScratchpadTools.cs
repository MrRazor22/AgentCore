using System.ComponentModel;

namespace AgentCore.Memory;

/// <summary>
/// Tools for the agent to manage its scratchpad (in-context working memory).
/// Matches Letta's core_memory_append, core_memory_replace pattern.
/// </summary>
public sealed class ScratchpadTools(IList<CoreMemoryBlock> blocks)
{
    [Description("Append content to a scratchpad section. Use to add new information without overwriting existing notes.")]
    public string ScratchpadAppend(
        [Description("The label of the scratchpad section (e.g., 'notes', 'work', 'planning')")] string label,
        [Description("The content to append")] string content)
    {
        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

        if (block == null)
            return $"Error: No scratchpad section found with label '{label}'. Available writable sections: {string.Join(", ", blocks.Where(b => !b.ReadOnly).Select(b => b.Label))}";

        if (block.ReadOnly)
            return $"Error: The section '{label}' is read-only and cannot be modified.";

        var newContent = block.Value + "\n" + content;
        
        // Check limit before updating
        if (block.Limit > 0 && newContent.Length > block.Limit)
            return $"Error: Appending would exceed limit ({block.Limit} characters). Current: {block.Value.Length}, Adding: {content.Length}. Summarize or use ScratchpadReplace instead.";

        block.Value = newContent;
        return $"Success: Appended to '{label}'. New size: {block.Value.Length} characters.";
    }

    [Description("Replace entire content of a scratchpad section. Use to summarize or completely rewrite notes.")]
    public string ScratchpadReplace(
        [Description("The label of the scratchpad section to replace")] string label,
        [Description("The new content (replaces existing)")] string content)
    {
        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

        if (block == null)
            return $"Error: No scratchpad section found with label '{label}'. Available writable sections: {string.Join(", ", blocks.Where(b => !b.ReadOnly).Select(b => b.Label))}";

        if (block.ReadOnly)
            return $"Error: The section '{label}' is read-only and cannot be modified.";

        // Check limit before updating
        if (block.Limit > 0 && content.Length > block.Limit)
            return $"Error: Content ({content.Length} chars) exceeds limit ({block.Limit} chars). Summarize before storing.";

        block.Value = content;
        return $"Success: Replaced content in '{label}'. New size: {block.Value.Length} characters.";
    }

}
