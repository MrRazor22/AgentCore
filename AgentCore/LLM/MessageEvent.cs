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
public sealed record TextContentDelta(string Text) : IMessageEvent;
public sealed record TextContentEnd() : IMessageEvent;

// 2. Reasoning Content Streaming
public sealed record ReasoningContentDelta(string Thought) : IMessageEvent;
public sealed record ReasoningContentEnd() : IMessageEvent;

// 3. Tool Call Content Streaming
public sealed record ToolCallContentStart(string Id, string Name, int? Index = null) : IMessageEvent;
public sealed record ToolCallContentDelta(string Id, string Arguments, int? Index = null) : IMessageEvent;
public sealed record ToolCallContentEnd(string Id, int? Index = null) : IMessageEvent;

// 4. Telemetry Data DTO
public sealed record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0);
