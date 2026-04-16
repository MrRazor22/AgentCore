using AgentCore.Conversation;

namespace AgentCore.Memory;

/// <summary>
/// Simple Letta-style core memory. In-prompt scratchpads. Not semantic.
/// Implements IAgentMemory with simple behavior.
/// </summary>
public sealed class CoreMemory : IAgentMemory
{
    private readonly List<CoreMemoryBlock> _blocks = new();

    public CoreMemory(IEnumerable<CoreMemoryBlock>? blocks = null)
    {
        if (blocks != null)
            _blocks.AddRange(blocks);
    }

    /// <summary>Returns ALL blocks for injection. Not semantic search.</summary>
    public Task<IReadOnlyList<IContent>> RecallAsync(string query, CancellationToken ct = default)
    {
        var contents = _blocks
            .Where(b => !string.IsNullOrEmpty(b.Value))
            .Select(b => (IContent)new Text(b.ToLlmString()))
            .ToList();
        return Task.FromResult<IReadOnlyList<IContent>>(contents);
    }

    /// <summary>No-op. Agent writes to scratchpad via tools (ScratchpadAppend/Replace).</summary>
    public Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
        => Task.CompletedTask;

    internal IList<CoreMemoryBlock> GetBlocks() => _blocks;
}
