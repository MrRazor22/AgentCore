using System;

namespace AgentCore.Diagnostics;

/// <summary>Marker interface for all diagnostic events emitted by the tracing pipeline.</summary>
public interface IDiagnosticEvent
{
    string EventType { get; }
    int Version { get; }
    DateTime Timestamp { get; }
}

public sealed record TraceStartedEvent(
    TraceSnapshot Trace,
    DateTime Timestamp,
    int Version = 1
) : IDiagnosticEvent
{
    public string EventType => "TraceStarted";
}

public sealed record SpanStartedEvent(
    SpanSnapshot Span,
    DateTime Timestamp,
    int Version = 1
) : IDiagnosticEvent
{
    public string EventType => "SpanStarted";
}

public sealed record SpanFinishedEvent(
    SpanSnapshot Span,
    DateTime Timestamp,
    int Version = 1
) : IDiagnosticEvent
{
    public string EventType => "SpanFinished";
}

public sealed record TraceFinishedEvent(
    TraceSnapshot Trace,
    DateTime Timestamp,
    int Version = 1
) : IDiagnosticEvent
{
    public string EventType => "TraceFinished";
}
