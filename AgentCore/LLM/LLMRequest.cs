using AgentCore.Conversation;
using AgentCore.Tooling;
using System.Collections.Generic;

namespace AgentCore.LLM;

/// <summary>
/// Represents the clean, semantic request DTO for invoking the LLM, encapsulating all execution-scoped parameters.
/// </summary>
public readonly record struct LLMRequest(
    IReadOnlyList<Message> Messages,
    LLMOptions Options,
    IReadOnlyList<Tool>? Tools = null);
