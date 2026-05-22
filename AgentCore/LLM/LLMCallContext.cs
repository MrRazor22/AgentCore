using AgentCore.Conversation;
using System.Collections.Generic;

namespace AgentCore.LLM;

/// <summary>
/// Context provided to LLM middleware for processing a call.
/// </summary>
public sealed record LLMCallContext(
    IReadOnlyList<Message> Messages,
    LLMOptions Options,
    int StepIndex);
