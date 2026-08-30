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
public sealed record TextStart(int Index = 0) : IMessageEvent;
public sealed record TextDelta(int Index, string Text) : IMessageEvent;
public sealed record TextEnd(int Index = 0) : IMessageEvent;

// 2. Reasoning Content Streaming
public sealed record ReasoningStart(int Index = 0) : IMessageEvent;
public sealed record ReasoningDelta(int Index, string Thought) : IMessageEvent;
public sealed record ReasoningEnd(int Index = 0) : IMessageEvent;

// 3. Tool Call Content Streaming
public sealed record ToolCallStart(int Index, string Id, string Name) : IMessageEvent;
public sealed record ToolCallDelta(int Index, string Arguments) : IMessageEvent;
public sealed record ToolCallEnd(int Index) : IMessageEvent;

// 4. Telemetry Data DTO
public sealed record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0);
