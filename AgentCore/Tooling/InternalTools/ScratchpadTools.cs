using System.ComponentModel;

namespace AgentCore.Tooling.InternalTools;

/// <summary>
/// Tools for the agent to manage its own scratchpads (Letta pattern).
/// </summary>
public sealed class ScratchpadTools(IList<Scratchpad> blocks)
{
    [Description("Update a writable scratchpad. Use to store working notes, user preferences, or persona changes.")]
    public string UpdateScratchpad(
        [Description("The label of the scratchpad to update (e.g., 'scratchpad', 'persona')")] string label,
        [Description("The new content for the scratchpad")] string content)
    {
        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

        if (block == null)
            return $"Error: No scratchpad found with label '{label}'. Available writable scratchpads: {string.Join(", ", blocks.Where(b => !b.ReadOnly).Select(b => b.Label))}";

        if (block.ReadOnly)
            return $"Error: The scratchpad '{label}' is read-only and cannot be modified by the agent.";

        // Check limit before updating - return error if would exceed
        if (block.Limit > 0 && content.Length > block.Limit)
            return $"Error: Content length ({content.Length} characters) exceeds scratchpad limit ({block.Limit} characters). You must summarize or replace the existing content instead of appending.";

        block.Value = content;
        return $"Success: Scratchpad '{label}' updated. Current size: {block.Value.Length} characters.";
    }

    [Description("Read the current content of a specific scratchpad.")]
    public string ReadScratchpad([Description("The label of the scratchpad to read")] string label)
    {
        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (block == null) return $"Error: No scratchpad found with label '{label}'.";
        return block.Value;
    }
}
