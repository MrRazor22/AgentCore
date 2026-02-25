using AgentCore.Chat;

namespace AgentCore.LLM;

public abstract record LLMEvent;

public sealed record TextEvent(string Delta) : LLMEvent;

public sealed record ToolCallEvent(ToolCall Call) : LLMEvent;
