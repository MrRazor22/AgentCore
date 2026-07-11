using AgentCore.Conversation;
using AgentCore.Tokens;

namespace AgentCore.LLM;

public abstract record LLMEvent : AgentEvent;

public sealed record TextEvent(string Delta) : LLMEvent;

public sealed record ReasoningEvent(string Delta) : LLMEvent;

public sealed record ToolCallEvent(ToolCall Call) : LLMEvent;

public sealed record TokenUsageEvent(
    int InputTokens,
    int OutputTokens,
    int? ReasoningTokens = null
) : LLMEvent;

public sealed record MetaDataEvent(
    FinishReason FinishReason,
    string? ModelName,
    TimeSpan? Duration = null
) : LLMEvent;

public sealed record AssistantMessageEvent(Message Message) : LLMEvent;

