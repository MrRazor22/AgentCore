using AgentCore.Conversation;
using AgentCore.Tokens;

namespace AgentCore.LLM;

public abstract record LLMEvent : AgentEvent;

public sealed record TextEvent(string Delta) : LLMEvent;

public sealed record ReasoningEvent(string Delta) : LLMEvent;

public sealed record ToolCallEvent(ToolCall Call) : LLMEvent;

public sealed record TokenUsageEvent(TokenUsage Usage) : LLMEvent;
