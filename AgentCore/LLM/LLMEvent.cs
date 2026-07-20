using AgentCore.LLM.Chat;
namespace AgentCore.LLM;

public enum FinishReason { Stop, ToolCall, Cancelled }
public enum ToolCallMode { None, Auto, Required }

public abstract record LLMEvent;

public sealed record TokenUsage(
    int InputTokens,
    int OutputTokens,
    int? ReasoningTokens = null
) : LLMEvent;

public sealed record MetaDataEvent(
    FinishReason FinishReason,
    TimeSpan? Duration = null
) : LLMEvent;
