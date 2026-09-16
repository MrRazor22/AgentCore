using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Host.Abstractions;

namespace AgentCore.Host.Ipc;

public sealed class StdioIpcChannel : IIpcChannel
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task SendAsync(IpcMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOpts);
        await _writeLock.WaitAsync();
        try
        {
            await Console.Out.WriteLineAsync(json);
            await Console.Out.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async IAsyncEnumerable<IpcMessage> ReadMessagesAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && await Console.In.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            IpcMessage? msg = null;
            try { msg = JsonSerializer.Deserialize<IpcMessage>(line, JsonOpts); } catch { }
            if (msg != null) yield return msg;
        }
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
