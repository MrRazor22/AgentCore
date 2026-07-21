using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.Memory;

namespace AgentCore.Example;

public class PersistentMemoryLayer : IMemory
{
    private readonly IMemory _inner;
    private readonly string _filePath;
    private List<Message> _messages = new();

    public PersistentMemoryLayer(IMemory inner, string filePath)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await ReloadFromDiskAsync(null, ct);
    }

    public async Task ReloadFromDiskAsync(string? path = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(path) && !string.Equals(path, _filePath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(path, _filePath, overwrite: true);
        }

        if (File.Exists(_filePath))
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var loaded = JsonSerializer.Deserialize<List<Message>>(json) ?? new();
            await RestoreAsync(loaded, ct);
        }
        else
        {
            await ClearAsync(ct);
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        _messages.Clear();
        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct);
        await _inner.ClearAsync(ct);
    }

    public async Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default)
    {
        _messages = history?.ToList() ?? new List<Message>();
        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct);
        await _inner.RestoreAsync(_messages, ct);
    }

    public Task<List<Message>> PrepareAsync(
        Message userInput,
        CancellationToken ct = default)
    {
        return _inner.PrepareAsync(userInput, ct);
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        await _inner.RememberAsync(completedTurn, ct);

        _messages.AddRange(completedTurn);

        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    public IReadOnlyList<Message> GetLocalMessages() => _messages;

    public void SetLocalMessages(List<Message> messages)
    {
        _messages = messages.ToList();
    }
}
