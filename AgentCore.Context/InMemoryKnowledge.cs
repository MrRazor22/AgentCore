using AgentCore.Conversation;
using System.Collections.Concurrent;
using System.Text;

namespace AgentCore.Context;

/// <summary>
/// Simple in-memory volatile knowledge store.
/// </summary>
public sealed class InMemoryKnowledge : IMemory
{
    public string Name => "memory";
    public Role Role => Role.System;
    public int Priority => 80;

    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task RememberAsync(string key, string value, CancellationToken ct = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> RecallAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

    public Task<IReadOnlyDictionary<string, string>> RecallAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_store));

    public Task ForgetAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IContent>> GetContextAsync(CancellationToken ct = default)
    {
        if (_store.IsEmpty)
            return Task.FromResult<IReadOnlyList<IContent>>(Array.Empty<IContent>());

        var sb = new StringBuilder();
        sb.AppendLine("# Agent Memory");
        foreach (var (key, value) in _store)
            sb.AppendLine($"- **{key}**: {value}");

        return Task.FromResult<IReadOnlyList<IContent>>([new Text(sb.ToString())]);
    }
}
