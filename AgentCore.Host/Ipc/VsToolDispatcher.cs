using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Host.Abstractions;

namespace AgentCore.Host.Ipc;

public sealed class VsToolDispatcher(IIpcChannel channel) : IVsToolDispatcher
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public async Task<string> InvokeAsync(string tool, object toolArgs)
    {
        string id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var argsElement = JsonSerializer.SerializeToElement(toolArgs);
        await channel.SendAsync(new("tool_call", Id: id, Name: tool, Args: argsElement));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var reg = timeoutCts.Token.Register(() => tcs.TrySetException(new TimeoutException($"Tool '{tool}' timed out.")));

        return await tcs.Task;
    }

    public bool TryHandleResult(string id, string? result, bool isError)
    {
        if (!_pending.TryRemove(id, out var tcs)) return false;

        if (isError) tcs.TrySetException(new InvalidOperationException(result));
        else tcs.TrySetResult(result ?? string.Empty);
        return true;
    }
}
