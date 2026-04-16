using AgentCore.Conversation;
using Microsoft.Extensions.Logging;

namespace AgentCore.Memory;

/// <summary>
/// Simple Letta-style core memory. In-prompt scratchpads. Not semantic.
/// Implements IAgentMemory with simple behavior.
/// </summary>
public sealed class CoreMemory : IAgentMemory
{
    private readonly List<CoreMemoryBlock> _blocks = new();
    private readonly ILogger<CoreMemory> _logger;

    public CoreMemory(IEnumerable<CoreMemoryBlock>? blocks = null, ILogger<CoreMemory>? logger = null)
    {
        if (blocks != null)
            _blocks.AddRange(blocks);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CoreMemory>.Instance;
    }

    /// <summary>Returns ALL blocks for injection. Not semantic search.</summary>
    public Task<IReadOnlyList<IContent>> RecallAsync(string query, CancellationToken ct = default)
    {
        _logger.LogDebug("Memory recall: QueryLength={QueryLen} ReturningBlocks={BlockCount}", query?.Length ?? 0, _blocks.Count);

        var contents = _blocks
            .Where(b => !string.IsNullOrEmpty(b.Value))
            .Select(b => (IContent)new Text(b.ToLlmString()))
            .ToList();

        if (contents.Count > 0)
        {
            var totalLength = contents.Sum(c => c.ForLlm()?.Length ?? 0);
            _logger.LogTrace("Memory recall result: TotalContentLength={TotalLen} BlockNames={BlockNames}",
                totalLength, string.Join(", ", _blocks.Where(b => !string.IsNullOrEmpty(b.Value)).Select(b => b.Label)));
        }

        return Task.FromResult<IReadOnlyList<IContent>>(contents);
    }

    /// <summary>No-op. Agent writes to scratchpad via tools (ScratchpadAppend/Replace).</summary>
    public Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        _logger.LogDebug("Memory retain: MessageCount={MsgCount} (no-op for CoreMemory)", messages.Count);
        return Task.CompletedTask;
    }

    internal IList<CoreMemoryBlock> GetBlocks() => _blocks;
}
