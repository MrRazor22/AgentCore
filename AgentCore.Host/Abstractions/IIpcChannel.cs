using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Host.Ipc;

namespace AgentCore.Host.Abstractions;

public interface IIpcChannel : IAsyncDisposable
{
    Task SendAsync(IpcMessage message);
    IAsyncEnumerable<IpcMessage> ReadMessagesAsync(CancellationToken ct = default);
}
