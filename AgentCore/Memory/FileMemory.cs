using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Memory;

public class FileMemory : IMemory
{
    private readonly IMemory _inner;
    private readonly string _filePath;
    private readonly List<Message> _messages = new();

    public FileMemory(IMemory inner, string filePath)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public IReadOnlyList<Message> Messages => _messages;

    public Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default)
    {
        return _inner.PrepareAsync(newInput, ct);
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        await _inner.RememberAsync(completedTurn, ct).ConfigureAwait(false);
        _messages.AddRange(completedTurn);
        await SaveToDiskAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        _messages.Clear();
        await SaveToDiskAsync(ct).ConfigureAwait(false);
        await _inner.ClearAsync(ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default)
    {
        _messages.Clear();
        if (history != null && history.Count > 0)
        {
            _messages.AddRange(history);
        }
        await SaveToDiskAsync(ct).ConfigureAwait(false);
        await _inner.RestoreAsync(_messages, ct).ConfigureAwait(false);
    }

    private async Task SaveToDiskAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }
}
