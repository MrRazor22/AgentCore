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
public sealed class FileWalStore(string storageDirectory, string sessionId, JsonSerializerOptions? options = null) : IWalStore
{
    private readonly JsonSerializerOptions _options = options ?? StoreJson.Options;
    private readonly string _path = Path.Combine(
        !string.IsNullOrWhiteSpace(storageDirectory) ? storageDirectory : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory)),
        $"{string.Join("_", (string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId)) : sessionId).Split(Path.GetInvalidFileNameChars()))}.wal");
    private StreamWriter? _writer;

    public async Task AppendAsync(IMessageEvent evt, CancellationToken ct = default)
    {
        if (_writer == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            _writer = new StreamWriter(_path, append: true) { AutoFlush = true };
        }
        await _writer.WriteLineAsync(JsonSerializer.Serialize(evt, _options).AsMemory(), ct).ConfigureAwait(false);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _writer?.Dispose();
        _writer = null;
        try { File.Delete(_path); } catch { }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IMessageEvent> RecoverAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(_path)) yield break;
        using var reader = new StreamReader(_path);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line) && JsonSerializer.Deserialize<IMessageEvent>(line, _options) is { } evt)
                yield return evt;
        }
    }
}
