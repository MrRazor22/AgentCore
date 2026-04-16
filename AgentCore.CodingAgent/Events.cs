using AgentCore.Conversation;
using AgentCore.Execution;

namespace AgentCore.CodingAgent;

public record AgentMessageEvent(Message Message) : AgentEvent;
public record AgentReasoningEvent(string Reasoning) : AgentEvent;
public record AgentFinalResultEvent(object? Result) : AgentEvent;

public sealed record CodeExecutionEvent(string Code, CodeOutput Result) : AgentEvent;

public sealed record CodeErrorEvent(string Code, string Error) : AgentEvent;
