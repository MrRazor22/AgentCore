using System.Text.Json;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

public sealed class JsonFileChatStore(string storageDirectory) : IChatStore
{
    private readonly string _storageDirectory = !string.IsNullOrWhiteSpace(storageDirectory)
        ? storageDirectory
        : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory));

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<Message>?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var filePath = GetFilePath(sessionId);
        if (!File.Exists(filePath)) return null;

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<Message>>(stream, SerializerOptions, ct).ConfigureAwait(false);
    }

    public async Task SaveAsync(string sessionId, IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_storageDirectory);
        var filePath = GetFilePath(sessionId);
        var tempPath = filePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, messages, SerializerOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, filePath, overwrite: true);
    }

    private string GetFilePath(string sessionId)
    {
        var sanitized = string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storageDirectory, $"{sanitized}.json");
    }
}
