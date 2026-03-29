using AgentCore.Conversation;
using AgentCore.Tooling;

namespace AgentCore;

public abstract record AgentEvent;

public sealed record AgentToolResultEvent(ToolResult Result) : AgentEvent;
