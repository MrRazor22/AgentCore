using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public class TraceDispatcher
{
    private readonly IEnumerable<ITraceExporter> _exporters;

    public TraceDispatcher(IEnumerable<ITraceExporter> exporters)
    {
        _exporters = exporters;
    }

    public async Task DispatchAsync(TraceSnapshot trace, CancellationToken ct = default)
    {
        foreach (var exporter in _exporters)
        {
            try
            {
                await exporter.ExportAsync(trace, ct);
            }
            catch
            {
                // Isolate failing exporters so they don't crash the pipeline
            }
        }
    }
}
