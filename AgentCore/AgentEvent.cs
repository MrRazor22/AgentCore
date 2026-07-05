using System;
using AgentCore.Conversation;
using AgentCore.Tooling;

namespace AgentCore;

public abstract record AgentEvent;

public sealed record AgentToolResultEvent(ToolResult Result) : AgentEvent;

public sealed record AgentErrorEvent(Exception Error) : AgentEvent;
