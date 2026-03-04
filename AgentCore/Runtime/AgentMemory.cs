using System.Collections.Concurrent;
using AgentCore.Chat;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AgentCore.Runtime;

public interface IAgentMemory
{
    Task<IList<Message>> RecallAsync(string sessionId);
    Task UpdateAsync(string sessionId, IList<Message> messages);
    Task ClearAsync(string sessionId);
}

public sealed class FileMemoryOptions
{
    public string? PersistDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCore");
    public int MaxChatHistory { get; set; }
}

public sealed class FileMemory(IOptions<FileMemoryOptions> options) : IAgentMemory
{
    private readonly FileMemoryOptions _options = options?.Value ?? new FileMemoryOptions();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public FileMemory() : this(null) { }

    private SemaphoreSlim GetLock(string sessionId) => _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    public async Task<IList<Message>> RecallAsync(string sessionId)
    {
        if (_options.PersistDir == null) return new List<Message>();

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (!File.Exists(file)) return new List<Message>();

        var semaphore = GetLock(sessionId);
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            return Extensions.FromJson(json);
        }
        catch
        {
            return new List<Message>();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task UpdateAsync(string sessionId, IList<Message> messages)
    {
        if (_options.PersistDir == null) return;

        Directory.CreateDirectory(_options.PersistDir);
        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        
        var semaphore = GetLock(sessionId);
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = messages.ToJson();
            await File.WriteAllTextAsync(file, json).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task ClearAsync(string sessionId)
    {
        if (_options.PersistDir == null) return Task.CompletedTask;

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (File.Exists(file)) File.Delete(file);

        return Task.CompletedTask;
    }
}
