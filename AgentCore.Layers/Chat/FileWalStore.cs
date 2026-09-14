using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Layers.Chat;

public class FileWalStore(string storageDirectory) : IWalStore
{
    private readonly string _dir = !string.IsNullOrWhiteSpace(storageDirectory)
        ? storageDirectory
        : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory));

    public Task AppendAsync(string sessionId, IMessageEvent evt, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_dir);
        var line = JsonSerializer.Serialize(evt);
        return File.AppendAllLinesAsync(GetPath(sessionId), [line], ct);
    }

    public Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        var path = GetPath(sessionId);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IMessageEvent> RecoverAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path)) yield break;

        var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonSerializer.Deserialize<IMessageEvent>(line) is { } evt)
                yield return evt;
        }
    }

    private string GetPath(string sessionId) =>
        Path.Combine(_dir, $"{string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()))}.wal");
}
