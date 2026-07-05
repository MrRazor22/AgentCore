using AgentCore.Conversation;
using AgentCore.Tokens;

namespace AgentCore;

/// <summary>
/// Represents the complete result of an agent invocation "turn".
/// </summary>
/// <param name="Content">The final content produced during this turn.</param>
/// <param name="Usage">Aggregated token usage for all LLM calls in this turn.</param>
public sealed record AgentResponse(
    IContent Content,
    TokenUsage Usage
)
{
    /// <summary>
    /// Gets the text representation of the response content.
    /// </summary>
    public string Text => Content is Text text ? text.Value : "";

    public override string ToString() => Content.ForLlm();
}
