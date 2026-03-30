using AgentCore.Conversation;
using AgentCore.Tokens;

namespace AgentCore;

/// <summary>
/// Represents the complete result of an agent invocation "turn".
/// </summary>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Messages">The list of messages added during this turn (User, Assistant steps, Tool steps).</param>
/// <param name="Usage">Aggregated token usage for all LLM calls in this turn.</param>
public sealed record AgentResponse(
    string SessionId,
    IReadOnlyList<Message> Messages,
    TokenUsage Usage
)
{
    /// <summary>
    /// Gets the text from the final assistant message in the turn, if any.
    /// </summary>
    public string Text => Messages
        .Where(m => m.Role == Role.Assistant)
        .LastOrDefault()?.Contents
        .OfType<Text>()
        .LastOrDefault()?.Value ?? "";

    public override string ToString() => Text;
}
