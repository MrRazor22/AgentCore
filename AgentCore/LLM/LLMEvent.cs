using AgentCore.Conversation;
using AgentCore.Execution;
using AgentCore.Tokens;

namespace AgentCore.LLM;

public abstract record LLMEvent : AgentEvent;

public sealed record TextEvent(string Delta) : LLMEvent;

public sealed record ReasoningEvent(string Delta) : LLMEvent;

public sealed record ToolCallEvent(ToolCall Call) : LLMEvent;

public sealed record LLMMetaEvent(
    TokenUsage Usage,
    FinishReason FinishReason,
    string ModelName,
    TimeSpan? Duration = null,
    int ToolSchemaTokens = 0,
    Dictionary<string, object>? Extra = null
) : LLMEvent;
