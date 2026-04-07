using AgentCore.Conversation;

namespace AgentCore.CodingAgent;

public record AgentMessageEvent(Message Message) : AgentCore.AgentEvent;
public record AgentReasoningEvent(string Reasoning) : AgentCore.AgentEvent;
public record AgentFinalResultEvent(object? Result) : AgentCore.AgentEvent;

public sealed record CodeExecutionEvent(string Code, CodeOutput Result) : AgentCore.AgentEvent;

public sealed record CodeErrorEvent(string Code, string Error) : AgentCore.AgentEvent;
