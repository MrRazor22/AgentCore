using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Layers.Context.Store;
public interface IWalStore
{
    Task AppendAsync(IMessageEvent evt, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> RecoverAsync(CancellationToken ct = default);
}
public class FileWalStore(string storageDirectory, string sessionId, JsonSerializerOptions? options = null) : IWalStore
{
    private readonly JsonSerializerOptions _options = options ?? StoreJson.Options;
    private readonly string _path = Path.Combine(
        !string.IsNullOrWhiteSpace(storageDirectory) ? storageDirectory : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory)),
        $"{string.Join("_", (string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId)) : sessionId).Split(Path.GetInvalidFileNameChars()))}.wal");

    public Task AppendAsync(IMessageEvent evt, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var line = JsonSerializer.Serialize(evt, _options);
        return File.AppendAllLinesAsync(_path, [line], ct);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IMessageEvent> RecoverAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(_path)) yield break;

        var lines = await File.ReadAllLinesAsync(_path, ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonSerializer.Deserialize<IMessageEvent>(line, _options) is { } evt)
                yield return evt;
        }
    }
}
