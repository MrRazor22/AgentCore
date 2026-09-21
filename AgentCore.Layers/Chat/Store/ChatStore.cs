using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context.Store;
public interface IChatStore
{
    Task<IReadOnlyList<Message>?> LoadAsync(CancellationToken ct = default);
    Task AppendAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);
}
public sealed class FileChatStore(string storageDirectory, string sessionId, JsonSerializerOptions? options = null) : IChatStore
{
    private readonly JsonSerializerOptions _options = options ?? StoreJson.Options;
    private readonly string _path = Path.Combine(
        !string.IsNullOrWhiteSpace(storageDirectory) ? storageDirectory : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory)),
        $"{string.Join("_", (string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId)) : sessionId).Split(Path.GetInvalidFileNameChars()))}.jsonl");

    public async Task<IReadOnlyList<Message>?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;

        var lines = await File.ReadAllLinesAsync(_path, ct).ConfigureAwait(false);
        var messages = new List<Message>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonSerializer.Deserialize<Message>(line, _options) is { } m)
                messages.Add(m);
        }
        return messages;
    }

    public Task AppendAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return Task.CompletedTask;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var lines = messages.Select(m => JsonSerializer.Serialize(m, _options));
        return File.AppendAllLinesAsync(_path, lines, ct);
    }
}

