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

public sealed class InMemoryMemory : IAgentMemory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IList<Message>> _store = new();
    
    public Task<IList<Message>> RecallAsync(string sessionId) => 
        Task.FromResult(_store.TryGetValue(sessionId, out var msgs) ? msgs.Clone() : new List<Message>());

    public Task UpdateAsync(string sessionId, IList<Message> messages)
    {
        _store[sessionId] = messages.Clone();
        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}

public sealed class FileMemoryOptions
{
    public string? PersistDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCore");
}

public sealed class FileMemory(IOptions<FileMemoryOptions>? options) : IAgentMemory
{
    private readonly FileMemoryOptions _options = options?.Value ?? new FileMemoryOptions();
    
    // Lock striping: minimal memory overhead, allows high concurrency across different sessions
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public FileMemory() : this(null) { }

    private SemaphoreSlim GetLock(string sessionId) 
        => _locks[Math.Abs(sessionId.GetHashCode()) % _locks.Length];

    public async Task<IList<Message>> RecallAsync(string sessionId)
    {
        if (_options.PersistDir == null) return new List<Message>();

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (!File.Exists(file)) return new List<Message>();

        var _lock = GetLock(sessionId);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            return Extensions.FromJson(json);
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            return new List<Message>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(string sessionId, IList<Message> messages)
    {
        if (_options.PersistDir == null) return;

        Directory.CreateDirectory(_options.PersistDir);
        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        var tmpFile = file + ".tmp";
        
        var _lock = GetLock(sessionId);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = messages.ToJson();
            await File.WriteAllTextAsync(tmpFile, json).ConfigureAwait(false);
            File.Move(tmpFile, file, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(string sessionId)
    {
        if (_options.PersistDir == null) return;

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        
        var _lock = GetLock(sessionId);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        finally
        {
            _lock.Release();
        }
    }
}
