using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public class Tracer : IAgentTracer
{
    private readonly ChannelWriter<TraceSnapshot> _channel;
    private readonly TracerOptions _options;
    
    private static readonly AsyncLocal<IAgentTrace?> CurrentTrace = new();
    private static readonly AsyncLocal<IAgentSpan?> CurrentSpan = new();

    public Tracer(ChannelWriter<TraceSnapshot> channel, TracerOptions options)
    {
        _channel = channel;
        _options = options;
    }

    public static IAgentTrace? ActiveTrace => CurrentTrace.Value;
    public static IAgentSpan? ActiveSpan => CurrentSpan.Value;

    public IAgentTrace StartTrace(string traceName, string sessionId, string? conversationId = null, string? agentName = null)
    {
        var trace = new AgentTrace(
            Guid.NewGuid().ToString("N"),
            traceName,
            sessionId,
            conversationId,
            agentName ?? "Unknown",
            this);
            
        CurrentTrace.Value = trace;
        // When starting a new trace, clear any active span from a previous trace context
        CurrentSpan.Value = null;
        
        _ = NotifyTraceStartedAsync(trace.CreateStartSnapshot());
        
        return trace;
    }

    internal void SetCurrentSpan(IAgentSpan? span)
    {
        CurrentSpan.Value = span;
    }

    internal void SetCurrentTrace(IAgentTrace? trace)
    {
        CurrentTrace.Value = trace;
    }

    internal async ValueTask NotifyTraceStartedAsync(TraceSnapshot snapshot)
    {
        foreach (var observer in _options.Observers)
        {
            try { await observer.OnTraceStartedAsync(snapshot); } catch { /* ignore */ }
        }
    }

    internal async ValueTask NotifySpanStartedAsync(SpanSnapshot snapshot)
    {
        foreach (var observer in _options.Observers)
        {
            try { await observer.OnSpanStartedAsync(snapshot); } catch { /* ignore */ }
        }
    }

    internal async ValueTask NotifySpanFinishedAsync(SpanSnapshot snapshot)
    {
        foreach (var observer in _options.Observers)
        {
            try { await observer.OnSpanFinishedAsync(snapshot); } catch { /* ignore */ }
        }
    }

    internal async ValueTask NotifyTraceFinishedAsync(TraceSnapshot snapshot)
    {
        foreach (var observer in _options.Observers)
        {
            try { await observer.OnTraceFinishedAsync(snapshot); } catch { /* ignore */ }
        }
        
        // Queue for background processing
        _channel.TryWrite(snapshot);
    }
}

internal sealed class AgentTrace : IAgentTrace
{
    private readonly Tracer _tracer;
    private readonly IAgentTrace? _parentTraceToRestore;
    private readonly ConcurrentDictionary<string, object?> _attributes = new();
    private readonly ConcurrentBag<SpanSnapshot> _spans = new();
    private readonly DateTime _start;
    
    private SpanStatus _status = SpanStatus.Success;
    
    public string TraceId { get; }
    public string SessionId { get; }
    public string Name { get; }
    public string? ConversationId { get; }
    public string AgentName { get; }

    public AgentTrace(string traceId, string name, string sessionId, string? conversationId, string agentName, Tracer tracer)
    {
        TraceId = traceId;
        Name = name;
        SessionId = sessionId;
        ConversationId = conversationId;
        AgentName = agentName;
        _tracer = tracer;
        _start = DateTime.UtcNow;
        _parentTraceToRestore = Tracer.ActiveTrace;
    }

    public TraceSnapshot CreateStartSnapshot()
    {
        return new TraceSnapshot(
            TraceId,
            Name,
            SessionId,
            ConversationId,
            AgentName,
            _start,
            null,
            true,
            new Dictionary<string, object?>(_attributes),
            new List<SpanSnapshot>()
        );
    }

    public IAgentSpan StartSpan(string name, AgentSpanKind kind, string? input = null)
    {
        var parentSpan = Tracer.ActiveSpan;
        var span = new AgentSpan(Guid.NewGuid().ToString("N"), parentSpan?.SpanId, TraceId, name, kind, input, _tracer, this);
        _tracer.SetCurrentSpan(span);
        return span;
    }

    public void SetAttribute(string key, object? value)
    {
        _attributes[key] = value;
    }

    public void SetStatus(SpanStatus status, string? message = null)
    {
        _status = status;
        if (message != null)
        {
            SetAttribute("error.message", message);
        }
    }

    internal void AddCompletedSpan(SpanSnapshot snapshot)
    {
        _spans.Add(snapshot);
    }

    public void Dispose()
    {
        var end = DateTime.UtcNow;
        var snapshot = new TraceSnapshot(
            TraceId,
            Name,
            SessionId,
            ConversationId,
            AgentName,
            _start,
            end,
            _status == SpanStatus.Success,
            new Dictionary<string, object?>(_attributes),
            _spans.OrderBy(s => s.Start).ToList()
        );

        _ = _tracer.NotifyTraceFinishedAsync(snapshot);
        _tracer.SetCurrentTrace(_parentTraceToRestore);
    }
}

internal sealed class AgentSpan : IAgentSpan
{
    private readonly Tracer _tracer;
    private readonly AgentTrace _trace;
    private readonly IAgentSpan? _parentSpanToRestore;
    private readonly ConcurrentDictionary<string, object?> _attributes = new();
    private readonly ConcurrentBag<SpanEvent> _events = new();
    private readonly DateTime _start;
    private readonly Stopwatch _stopwatch;
    private readonly string? _input;
    
    private SpanStatus _status = SpanStatus.Success;
    private string? _statusMessage;
    private string? _output;

    public string SpanId { get; }
    public string? ParentSpanId { get; }
    public string TraceId { get; }
    public string Name { get; }
    public AgentSpanKind Kind { get; }

    public AgentSpan(string spanId, string? parentSpanId, string traceId, string name, AgentSpanKind kind, string? input, Tracer tracer, AgentTrace trace)
    {
        SpanId = spanId;
        ParentSpanId = parentSpanId;
        TraceId = traceId;
        Name = name;
        Kind = kind;
        _input = input;
        _tracer = tracer;
        _trace = trace;
        
        _start = DateTime.UtcNow;
        _stopwatch = Stopwatch.StartNew();
        _parentSpanToRestore = Tracer.ActiveSpan;
        
        _ = _tracer.NotifySpanStartedAsync(CreateSnapshot(null, null));
    }

    public void SetAttribute(string key, object? value)
    {
        _attributes[key] = value;
    }

    public void AddEvent(string name, IReadOnlyDictionary<string, object?>? attributes = null)
    {
        var dict = attributes == null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(attributes);
        _events.Add(new SpanEvent(name, DateTime.UtcNow, dict));
    }

    public void SetStatus(SpanStatus status, string? message = null)
    {
        _status = status;
        _statusMessage = message;
    }

    public void Fail(Exception exception)
    {
        SetStatus(SpanStatus.Error, exception.Message);
        SetAttribute("error.type", exception.GetType().Name);
        SetAttribute("error.stacktrace", exception.StackTrace);
    }
    
    public void SetOutput(string output)
    {
        _output = output;
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        var end = DateTime.UtcNow;
        var snapshot = CreateSnapshot(end, _stopwatch.Elapsed.TotalMilliseconds);
        
        _trace.AddCompletedSpan(snapshot);
        _ = _tracer.NotifySpanFinishedAsync(snapshot);
        _tracer.SetCurrentSpan(_parentSpanToRestore);
    }

    private SpanSnapshot CreateSnapshot(DateTime? end, double? durationMs)
    {
        return new SpanSnapshot(
            SpanId,
            ParentSpanId,
            TraceId,
            Name,
            Kind,
            _start,
            end,
            durationMs,
            _input,
            _output,
            _status,
            _statusMessage,
            new Dictionary<string, object?>(_attributes),
            _events.OrderBy(e => e.Timestamp).ToList()
        );
    }
}
