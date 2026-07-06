using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public interface ITraceReceiver
{
    Task ReceiveEventAsync(IDiagnosticEvent evt, CancellationToken ct = default);
    Task ReceiveTraceAsync(TraceSnapshot trace, CancellationToken ct = default);
}

public sealed class DefaultTraceReceiver : ITraceReceiver
{
    private readonly ITraceStore _store;
    private readonly TraceEventHub _hub;

    public DefaultTraceReceiver(ITraceStore store, TraceEventHub hub)
    {
        _store = store;
        _hub = hub;
    }

    public async Task ReceiveEventAsync(IDiagnosticEvent evt, CancellationToken ct = default)
    {
        await _hub.BroadcastAsync(evt);
    }

    public async Task ReceiveTraceAsync(TraceSnapshot trace, CancellationToken ct = default)
    {
        await _store.SaveAsync(trace, ct);
    }
}
