using System.Text.Json;
using AgentCore.Layers.Chat;
using AgentCore.LLM.Chat;

namespace CodeSharp.Storage;

public sealed class JsonLinesChatStore(string storageDirectory) : IChatStore
{
    private readonly string _storageDirectory = !string.IsNullOrWhiteSpace(storageDirectory)
        ? storageDirectory
        : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory));

    public async Task<IReadOnlyList<Message>?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var filePath = GetFilePath(sessionId);
        if (!File.Exists(filePath)) return null;

        var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
        var messages = new List<Message>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var msg = JsonSerializer.Deserialize<Message>(line);
            if (msg != null) messages.Add(msg);
        }

        return messages;
    }

    public Task AppendAsync(string sessionId, IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return Task.CompletedTask;

        Directory.CreateDirectory(_storageDirectory);
        var lines = messages.Select(m => JsonSerializer.Serialize(m));
        return File.AppendAllLinesAsync(GetFilePath(sessionId), lines, ct);
    }

    private string GetFilePath(string sessionId)
    {
        var sanitized = string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storageDirectory, $"{sanitized}.jsonl");
    }
}
