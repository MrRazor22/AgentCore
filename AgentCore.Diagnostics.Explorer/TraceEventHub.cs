using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public sealed class TraceEventHub
{
    private readonly ConcurrentDictionary<string, Func<IDiagnosticEvent, Task>> _clients = new();

    public void Subscribe(string clientId, Func<IDiagnosticEvent, Task> handler)
    {
        _clients[clientId] = handler;
    }

    public void Unsubscribe(string clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    public async Task BroadcastAsync(IDiagnosticEvent evt)
    {
        foreach (var client in _clients.Values)
        {
            try { await client(evt); } catch { }
        }
    }
}
