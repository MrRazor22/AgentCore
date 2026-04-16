using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AgentCore.Memory;

/// <summary>
/// Tools for the agent to manage its scratchpad (in-context working memory).
/// Matches Letta's core_memory_append, core_memory_replace pattern.
/// </summary>
public sealed class ScratchpadTools(IList<CoreMemoryBlock> blocks, ILogger<ScratchpadTools>? logger = null)
{
    private readonly ILogger<ScratchpadTools> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ScratchpadTools>.Instance;
    [Description("Append content to a scratchpad section. Use to add new information without overwriting existing notes.")]
    public string ScratchpadAppend(
        [Description("The label of the scratchpad section (e.g., 'notes', 'work', 'planning')")] string label,
        [Description("The content to append")] string content)
    {
        _logger.LogDebug("ScratchpadAppend: Label={Label} ContentLength={Len}", label, content.Length);

        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

        if (block == null)
        {
            _logger.LogWarning("ScratchpadAppend failed: Label={Label} BlockNotFound", label);
            return $"Error: No scratchpad section found with label '{label}'. Available writable sections: {string.Join(", ", blocks.Where(b => !b.ReadOnly).Select(b => b.Label))}";
        }

        if (block.ReadOnly)
        {
            _logger.LogWarning("ScratchpadAppend failed: Label={Label} ReadOnly", label);
            return $"Error: The section '{label}' is read-only and cannot be modified.";
        }

        var newContent = block.Value + "\n" + content;

        // Check limit before updating
        if (block.Limit > 0 && newContent.Length > block.Limit)
        {
            _logger.LogWarning("ScratchpadAppend failed: Label={Label} LimitExceeded Current={Current} Adding={Adding} Limit={Limit}",
                label, block.Value.Length, content.Length, block.Limit);
            return $"Error: Appending would exceed limit ({block.Limit} characters). Current: {block.Value.Length}, Adding: {content.Length}. Summarize or use ScratchpadReplace instead.";
        }

        block.Value = newContent;
        _logger.LogDebug("ScratchpadAppend success: Label={Label} OldSize={Old} NewSize={New}",
            label, block.Value.Length - content.Length - 1, block.Value.Length);
        return $"Success: Appended to '{label}'. New size: {block.Value.Length} characters.";
    }

    [Description("Replace entire content of a scratchpad section. Use to summarize or completely rewrite notes.")]
    public string ScratchpadReplace(
        [Description("The label of the scratchpad section to replace")] string label,
        [Description("The new content (replaces existing)")] string content)
    {
        _logger.LogDebug("ScratchpadReplace: Label={Label} ContentLength={Len}", label, content.Length);

        var block = blocks.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

        if (block == null)
        {
            _logger.LogWarning("ScratchpadReplace failed: Label={Label} BlockNotFound", label);
            return $"Error: No scratchpad section found with label '{label}'. Available writable sections: {string.Join(", ", blocks.Where(b => !b.ReadOnly).Select(b => b.Label))}";
        }

        if (block.ReadOnly)
        {
            _logger.LogWarning("ScratchpadReplace failed: Label={Label} ReadOnly", label);
            return $"Error: The section '{label}' is read-only and cannot be modified.";
        }

        // Check limit before updating
        if (block.Limit > 0 && content.Length > block.Limit)
        {
            _logger.LogWarning("ScratchpadReplace failed: Label={Label} LimitExceeded Content={Content} Limit={Limit}",
                label, content.Length, block.Limit);
            return $"Error: Content ({content.Length} chars) exceeds limit ({block.Limit} chars). Summarize before storing.";
        }

        block.Value = content;
        _logger.LogDebug("ScratchpadReplace success: Label={Label} NewSize={New}", label, block.Value.Length);
        return $"Success: Replaced content in '{label}'. New size: {block.Value.Length} characters.";
    }

}
