using AgentCore.Conversation;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;

namespace AgentCore.Context;

/// <summary>
/// Persistent agent knowledge stored as a JSON file.
/// </summary>
public sealed class FileKnowledge : IMemory
{
    public string Name => "persistent-memory";
    public Role Role => Role.System;
    public int Priority => 80;

    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileKnowledge(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data != null)
            {
                foreach (var kv in data) _cache[kv.Key] = kv.Value;
            }
        }
        catch { }
    }

    private async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RememberAsync(string key, string value, CancellationToken ct = default)
    {
        _cache[key] = value;
        await SaveAsync();
    }

    public Task<string?> RecallAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_cache.TryGetValue(key, out var v) ? v : null);

    public Task<IReadOnlyDictionary<string, string>> RecallAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_cache));

    public async Task ForgetAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryRemove(key, out _)) await SaveAsync();
    }

    public Task<IReadOnlyList<IContent>> GetContextAsync(CancellationToken ct = default)
    {
        if (_cache.IsEmpty)
            return Task.FromResult<IReadOnlyList<IContent>>(Array.Empty<IContent>());

        var sb = new StringBuilder();
        sb.AppendLine("# Persistent Knowledge");
        foreach (var kv in _cache)
            sb.AppendLine($"- **{kv.Key}**: {kv.Value}");

        return Task.FromResult<IReadOnlyList<IContent>>([new Text(sb.ToString())]);
    }
}
