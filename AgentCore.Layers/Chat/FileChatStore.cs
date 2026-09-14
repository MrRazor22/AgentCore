using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

public class FileChatStore(string storageDirectory) : IChatStore
{
    private readonly string _dir = !string.IsNullOrWhiteSpace(storageDirectory)
        ? storageDirectory
        : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory));

    public async Task<IReadOnlyList<Message>?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path)) return null;

        var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
        var messages = new List<Message>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonSerializer.Deserialize<Message>(line) is { } m)
                messages.Add(m);
        }
        return messages;
    }

    public Task AppendAsync(string sessionId, IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return Task.CompletedTask;
        Directory.CreateDirectory(_dir);
        var lines = messages.Select(m => JsonSerializer.Serialize(m));
        return File.AppendAllLinesAsync(GetPath(sessionId), lines, ct);
    }

    private string GetPath(string sessionId) =>
        Path.Combine(_dir, $"{string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()))}.jsonl");
}

public class JsonLinesChatStore(string storageDirectory) : FileChatStore(storageDirectory);

