using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public class FilePersistentChatContext : ContextLayer
{
    private readonly string _filePath;
    private readonly List<Message> _messages = new();

    public FilePersistentChatContext(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public override IReadOnlyList<Message> Messages => base.Messages;

    public override async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await base.AddAsync(message, ct).ConfigureAwait(false);
        _messages.Add(message);
        await SaveToDiskAsync(ct).ConfigureAwait(false);
    }

    public override async Task ClearAsync(CancellationToken ct = default)
    {
        _messages.Clear();
        await SaveToDiskAsync(ct).ConfigureAwait(false);
        await base.ClearAsync(ct).ConfigureAwait(false);
    }

    public override async Task AddRangeAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        await base.AddRangeAsync(messages, ct).ConfigureAwait(false);
        if (messages != null)
        {
            _messages.AddRange(messages);
        }
        await SaveToDiskAsync(ct).ConfigureAwait(false);
    }

    private async Task SaveToDiskAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }
}
