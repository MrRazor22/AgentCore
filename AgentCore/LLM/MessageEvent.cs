using AgentCore.LLM.Chat;

namespace AgentCore.LLM;

/// <summary>
/// Root interface for any stream event emitted during an LLM response message generation.
/// </summary>
public interface IMessageEvent { }

// 0. Message Lifecycle Envelope
public sealed record MessageStart(
    Role Role = Role.Assistant,
    string? Id = null,
    string? Model = null
) : IMessageEvent;

public sealed record MessageEnd(
    string? FinishReason = null,
    TokenUsage? Usage = null
) : IMessageEvent;

// 1. Text Content Streaming
public sealed record TextContentStart(int Index = 0) : IMessageEvent;
public sealed record TextContentDelta(int Index, string Text) : IMessageEvent;
public sealed record TextContentEnd(int Index = 0) : IMessageEvent;

// 2. Reasoning Content Streaming
public sealed record ReasoningContentStart(int Index = 0) : IMessageEvent;
public sealed record ReasoningContentDelta(int Index, string Thought) : IMessageEvent;
public sealed record ReasoningContentEnd(int Index = 0) : IMessageEvent;

// 3. Tool Call Content Streaming
public sealed record ToolCallContentStart(int Index, string Id, string Name) : IMessageEvent;
public sealed record ToolCallContentDelta(int Index, string Arguments) : IMessageEvent;
public sealed record ToolCallContentEnd(int Index) : IMessageEvent;

// 4. Telemetry Data DTO
public sealed record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0);
