using AgentCore.Conversation;
using System.Collections.Generic;

namespace AgentCore;

/// <summary>
/// Represents the context for an invocation of the agent, providing read-only access to previous history.
/// </summary>
public sealed record AgentRequestContext(
    IContent Input,
    string SessionId,
    IReadOnlyList<Message> History);
