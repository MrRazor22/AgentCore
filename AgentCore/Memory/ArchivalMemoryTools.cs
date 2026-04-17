using System.ComponentModel;
using AgentCore.Conversation;
using Microsoft.Extensions.Logging;

namespace AgentCore.Memory;

/// <summary>
/// Exposes Letta-style `archival_memory_search` tool so the agent can actively search semantic memory
/// instead of relying exclusively on Auto-RAG (automatic context injection).
/// </summary>
public sealed class ArchivalMemoryTools
{
    private readonly IAgentMemory _memoryEngine;
    private readonly ILogger<ArchivalMemoryTools> _logger;

    public ArchivalMemoryTools(IAgentMemory memoryEngine, ILogger<ArchivalMemoryTools>? logger = null)
    {
        _memoryEngine = memoryEngine;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivalMemoryTools>.Instance;
    }

    [Description("Search infinite archival memory using semantic search. Use this when you don't know a fact, or when you need to research deep past knowledge about the user, project, or protocols. Only call this when requested or necessary.")]
    public async Task<string> ArchivalMemorySearch(
        [Description("The specific query or question to semantically search for in the database.")] string query)
    {
        _logger.LogInformation("Agent invoked ArchivalMemorySearch: Query={Query}", query);

        try
        {
            var results = await _memoryEngine.RecallAsync(query);
            
            if (results == null || results.Count == 0)
            {
                return "No relevant facts found in archival memory.";
            }

            var textLines = results.Select((r, i) => $"Result {i + 1}:\n{r.ForLlm()}");
            return string.Join("\n\n---\n\n", textLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ArchivalMemorySearch failed.");
            return $"Error performing archival search: {ex.Message}";
        }
    }
}
