using System;
using System.Collections.Generic;

namespace AgentCore.Diagnostics;

public enum SpanStatus
{
    Success,
    Error,
    Cancelled
}

public enum AgentSpanKind
{
    Agent,
    Llm,
    Tool,
    Memory,
    Workflow,
    Custom
}

public sealed record SpanEvent(
    string Name,
    DateTime Timestamp,
    IReadOnlyDictionary<string, object?> Attributes
);

public sealed record SpanSnapshot(
    string SpanId,
    string? ParentSpanId,
    string TraceId,
    string Name,
    AgentSpanKind Kind,
    DateTime Start,
    DateTime? End,
    double? DurationMs,
    string? Input,
    string? Output,
    SpanStatus Status,
    string? StatusMessage,
    IReadOnlyDictionary<string, object?> Attributes,
    IReadOnlyList<SpanEvent> Events
);

public sealed record TraceSnapshot(
    string TraceId,
    string Name,
    string SessionId,
    string? ConversationId,
    string AgentName,
    DateTime Start,
    DateTime? End,
    bool IsSuccess,
    IReadOnlyDictionary<string, object?> Attributes,
    IReadOnlyList<SpanSnapshot> Spans
)
{
    public const int CurrentSchemaVersion = 1;
}

public record TraceSummary(
    string TraceId,
    string Name,
    string SessionId,
    string AgentName,
    DateTime Start,
    double DurationMs,
    bool IsSuccess
);

public sealed record TraceQuery(
    int Skip = 0,
    int Take = 100,
    bool? Success = null,
    string? AgentName = null,
    string? SessionId = null,
    DateTime? From = null,
    DateTime? To = null
);
