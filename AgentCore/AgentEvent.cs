using System;
using AgentCore.Conversation;
using AgentCore.Tooling;

namespace AgentCore;

public abstract record AgentEvent;

public sealed record ToolResultEvent(ToolResult Result) : AgentEvent;

public sealed record ErrorEvent(Exception Error) : AgentEvent;
