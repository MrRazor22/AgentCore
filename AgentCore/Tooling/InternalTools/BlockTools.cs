using System.ComponentModel;

namespace AgentCore.Tooling.InternalTools;

/// <summary>
/// Tools for the agent to manage its own memory blocks (Letta pattern).
/// </summary>
public sealed class BlockTools(IList<MemoryBlock> blocks)
{
    [Description("Update a writable memory block. Use to store working notes, user preferences, or persona changes.")]
    public string UpdateBlock(
        [Description("The label of the block to update (e.g., 'scratchpad', 'persona')")] string label,
        [Description("The new content for the block")] string content)
    {
        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        
        if (block == null)
            return $"Error: No memory block found with label '{label}'. Available writable blocks: {string.Join(", ", blocks.Where(b => !b.ReadOnly).Select(b => b.Label))}";

        if (block.ReadOnly)
            return $"Error: The block '{label}' is read-only and cannot be modified by the agent.";

        block.Value = content;
        return $"Success: Memory block '{label}' updated. Current size: {block.Value.Length} characters.";
    }

    [Description("Read the current content of a specific memory block.")]
    public string ReadBlock([Description("The label of the block to read")] string label)
    {
        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (block == null) return $"Error: No block found with label '{label}'.";
        return block.Value;
    }
}
