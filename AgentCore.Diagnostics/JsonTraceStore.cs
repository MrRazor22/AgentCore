using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public class JsonTraceStore : ITraceStore, ITraceExporter
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonTraceStore(string directory)
    {
        _directory = directory;
        if (!Directory.Exists(_directory))
        {
            Directory.CreateDirectory(_directory);
        }
    }

    public async Task ExportAsync(TraceSnapshot trace, CancellationToken ct = default)
    {
        await SaveAsync(trace, ct);
    }

    public async ValueTask SaveAsync(TraceSnapshot trace, CancellationToken ct = default)
    {
        var path = Path.Combine(_directory, $"{trace.TraceId}.json");
        var tempPath = path + ".tmp";
        
        using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await JsonSerializer.SerializeAsync(fs, trace, Options, ct);
        fs.Close();
        
        File.Move(tempPath, path, true);
    }

    public async ValueTask<IReadOnlyList<TraceSummary>> QueryAsync(TraceQuery query, CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_directory, "*.json");
        var summaries = new List<TraceSummary>();
        
        foreach (var file in files)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                var trace = await JsonSerializer.DeserializeAsync<TraceSnapshot>(fs, Options, ct);
                
                if (trace != null)
                {
                    summaries.Add(new TraceSummary(
                        trace.TraceId,
                        trace.Name,
                        trace.SessionId,
                        trace.AgentName,
                        trace.Start,
                        trace.End.HasValue ? (trace.End.Value - trace.Start).TotalMilliseconds : 0,
                        trace.IsSuccess
                    ));
                }
            }
            catch
            {
                // ignore unreadable files
            }
        }
        
        var result = summaries.AsEnumerable();
        
        if (query.SessionId != null) result = result.Where(s => s.SessionId == query.SessionId);
        if (query.AgentName != null) result = result.Where(s => s.AgentName == query.AgentName);
        if (query.Success.HasValue) result = result.Where(s => s.IsSuccess == query.Success.Value);
        if (query.From.HasValue) result = result.Where(s => s.Start >= query.From.Value);
        if (query.To.HasValue) result = result.Where(s => s.Start <= query.To.Value);
        
        return result.OrderByDescending(s => s.Start).Skip(query.Skip).Take(query.Take).ToList();
    }

    public async ValueTask<TraceSnapshot?> GetAsync(string traceId, CancellationToken ct = default)
    {
        var path = Path.Combine(_directory, $"{traceId}.json");
        if (!File.Exists(path)) return null;
        
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return await JsonSerializer.DeserializeAsync<TraceSnapshot>(fs, Options, ct);
    }
}
