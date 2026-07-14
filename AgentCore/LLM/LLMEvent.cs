using AgentCore.LLM.Conversation;
namespace AgentCore.LLM;

public enum FinishReason { Stop, ToolCall, Cancelled }
public enum ToolCallMode { None, Auto, Required }

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



