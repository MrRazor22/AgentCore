using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public interface IAgentSpan : IDisposable
{
    string SpanId { get; }
    string? ParentSpanId { get; }
    string TraceId { get; }
    string Name { get; }
    AgentSpanKind Kind { get; }
    
    void SetAttribute(string key, object? value);
    void AddEvent(string name, IReadOnlyDictionary<string, object?>? attributes = null);
    void SetStatus(SpanStatus status, string? message = null);
    void Fail(Exception exception);
    void SetOutput(string output);
}

public interface IAgentTrace : IDisposable
{
    string TraceId { get; }
    string SessionId { get; }
    string Name { get; }
    
    IAgentSpan StartSpan(string name, AgentSpanKind kind, string? input = null);
    void SetAttribute(string key, object? value);
    void SetStatus(SpanStatus status, string? message = null);
}

public interface IAgentTracer
{
    IAgentTrace StartTrace(string traceName, string sessionId, string? conversationId = null, string? agentName = null);
}

public interface ITraceObserver
{
    ValueTask OnTraceStartedAsync(TraceSnapshot trace);
    ValueTask OnSpanStartedAsync(SpanSnapshot span);
    ValueTask OnSpanFinishedAsync(SpanSnapshot span);
    ValueTask OnTraceFinishedAsync(TraceSnapshot trace);
}

public interface ITraceExporter
{
    Task ExportAsync(TraceSnapshot trace, CancellationToken ct = default);
}

public interface ITraceStore
{
    ValueTask SaveAsync(TraceSnapshot trace, CancellationToken ct = default);
    ValueTask<IReadOnlyList<TraceSummary>> QueryAsync(TraceQuery query, CancellationToken ct = default);
    ValueTask<TraceSnapshot?> GetAsync(string traceId, CancellationToken ct = default);
}
