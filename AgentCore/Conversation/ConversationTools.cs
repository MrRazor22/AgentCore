using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AgentCore.Conversation;

/// <summary>
/// Exposes Letta-style `conversation_search` tool so the agent can read raw conversation logs
/// beyond its immediate context window.
/// </summary>
public sealed class ConversationTools
{
    private readonly IChat _chatStore;
    private readonly string _sessionId;
    private readonly ILogger<ConversationTools> _logger;

    public ConversationTools(IChat chatStore, string sessionId, ILogger<ConversationTools>? logger = null)
    {
        _chatStore = chatStore;
        _sessionId = sessionId;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ConversationTools>.Instance;
    }

    [Description("Search prior conversation history. Use this to lookup what was said hours or days ago when the current context window doesn't contain the answer.")]
    public async Task<string> ConversationSearch(
        [Description("The keyword or phrase to look for in past messages.")] string query,
        [Description("Maximum number of messages to return. Max 20.")] int limit = 10)
    {
        _logger.LogInformation("Agent invoked ConversationSearch: Query={Query}", query);

        try
        {
            /// NOTE: Pure IChat interface doesn't yet have keyword search natively, 
            /// so doing a naïve trailing search. For full scale, IChat should expose SearchAsync.
            var history = await _chatStore.RecallAsync(_sessionId);
            
            var matches = history
                .Where(m => string.Join("", m.Contents.Select(c => c.ForLlm())).Contains(query, StringComparison.OrdinalIgnoreCase))
                .TakeLast(limit)
                .ToList();

            if (matches.Count == 0)
            {
                return "No past messages found containing that query.";
            }

            var textLines = matches.Select(m => $"[{m.Role}]: {string.Join("", m.Contents.Select(c => c.ForLlm()))}");
            return string.Join("\n---\n", textLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConversationSearch failed.");
            return $"Error performing conversation search: {ex.Message}";
        }
    }
}
