using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Memory;

public class FileMemory : MemoryLayer
{
    private readonly string _filePath;
    private readonly List<Message> _messages = new();

    public FileMemory(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public override IReadOnlyList<Message> Messages => _messages;

    public override async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        await base.RememberAsync(completedTurn, ct).ConfigureAwait(false);
        _messages.AddRange(completedTurn);
        await SaveToDiskAsync(ct).ConfigureAwait(false);
    }

    public override async Task ClearAsync(CancellationToken ct = default)
    {
        _messages.Clear();
        await SaveToDiskAsync(ct).ConfigureAwait(false);
        await base.ClearAsync(ct).ConfigureAwait(false);
    }

    public override async Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default)
    {
        _messages.Clear();
        if (history != null && history.Count > 0)
        {
            _messages.AddRange(history);
        }
        await SaveToDiskAsync(ct).ConfigureAwait(false);
        await base.RestoreAsync(_messages, ct).ConfigureAwait(false);
    }

    private async Task SaveToDiskAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }
}
