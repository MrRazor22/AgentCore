using System.Collections.Generic;

namespace AgentCore.Diagnostics;

public class TracerOptions
{
    public IList<ITraceExporter> Exporters { get; } = new List<ITraceExporter>();
    public IList<ITraceObserver> Observers { get; } = new List<ITraceObserver>();
}
