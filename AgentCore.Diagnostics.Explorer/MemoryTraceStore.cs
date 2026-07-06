using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public sealed class MemoryTraceStore : ITraceStore
{
    private readonly ConcurrentDictionary<string, TraceSnapshot> _traces = new();

    public ValueTask SaveAsync(TraceSnapshot trace, CancellationToken ct = default)
    {
        _traces[trace.TraceId] = trace;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<TraceSummary>> QueryAsync(TraceQuery query, CancellationToken ct = default)
    {
        IEnumerable<TraceSnapshot> results = _traces.Values;

        if (query.Success.HasValue)
        {
            results = results.Where(t => t.IsSuccess == query.Success.Value);
        }
        if (!string.IsNullOrEmpty(query.AgentName))
        {
            results = results.Where(t => t.AgentName.Equals(query.AgentName, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(query.SessionId))
        {
            results = results.Where(t => t.SessionId.Equals(query.SessionId, StringComparison.OrdinalIgnoreCase));
        }
        if (query.From.HasValue)
        {
            results = results.Where(t => t.Start >= query.From.Value);
        }
        if (query.To.HasValue)
        {
            results = results.Where(t => t.End <= query.To.Value);
        }

        var summaries = results
            .OrderByDescending(t => t.Start)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(t => new TraceSummary(
                t.TraceId,
                t.Name,
                t.SessionId,
                t.AgentName,
                t.Start,
                t.End.HasValue ? (t.End.Value - t.Start).TotalMilliseconds : 0,
                t.IsSuccess
            ))
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<TraceSummary>>(summaries);
    }

    public ValueTask<TraceSnapshot?> GetAsync(string traceId, CancellationToken ct = default)
    {
        _traces.TryGetValue(traceId, out var trace);
        return ValueTask.FromResult(trace);
    }
}
