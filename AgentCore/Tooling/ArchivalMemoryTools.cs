using System.ComponentModel;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using Microsoft.Extensions.Logging;

namespace AgentCore.Tooling;

/// <summary>
/// Exposes Letta-style `archival_memory_search` tool so the agent can actively search semantic memory
/// instead of relying exclusively on Auto-RAG (automatic context injection).
/// Also includes ReflectAsync for deep synthesis over memories.
/// </summary>
public sealed class ArchivalMemoryTools
{
    private readonly IAgentMemory _memoryEngine;
    private readonly ILLMProvider _llm;
    private readonly ILogger<ArchivalMemoryTools> _logger;

    public ArchivalMemoryTools(
        IAgentMemory memoryEngine,
        ILLMProvider llm,
        ILogger<ArchivalMemoryTools>? logger = null)
    {
        _memoryEngine = memoryEngine;
        _llm = llm;
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

    [Description("Reflect deeply on a question using all stored memory and understanding. Creates a persistent observation by synthesizing recalled memories into a coherent answer.")]
    public async Task<string> Reflect(
        [Description("The question to reflect on and synthesize an answer for")] string query,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Agent invoked Reflect: Query={Query}", query);

        try
        {
            // Step 1: recall relevant memories
            var recalled = await _memoryEngine.RecallAsync(query, ct).ConfigureAwait(false);
            var memoryContext = recalled.Count > 0
                ? string.Join("\n", recalled.Select(c => c.ForLlm()))
                : "(no relevant memories found)";

            // Step 2: LLM synthesizes an answer
            var systemPrompt = "You are a thoughtful assistant that synthesizes information from memories to answer questions comprehensively.";
            var userMessage = $"## Recalled Memories\n{memoryContext}\n\n## Question\n{query}";

            var messages = new List<Message>
            {
                new(Role.System, new Text(systemPrompt)),
                new(Role.User, new Text(userMessage))
            };

            var answer = await CompleteLLMAsync(messages, ct).ConfigureAwait(false);
            _logger.LogInformation("Reflect completed successfully for query: {Query}", query);

            return answer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reflect failed.");
            return $"Error during reflection: {ex.Message}";
        }
    }

    private async Task<string> CompleteLLMAsync(IReadOnlyList<Message> messages, CancellationToken ct)
    {
        var options = new LLMOptions
        {
            ToolCallMode = ToolCallMode.None
        };

        var sb = new System.Text.StringBuilder();
        await foreach (var delta in _llm.StreamAsync(messages, options, tools: null, ct).ConfigureAwait(false))
        {
            if (delta is TextDelta td) sb.Append(td.Value);
        }
        return sb.ToString().Trim();
    }
}
