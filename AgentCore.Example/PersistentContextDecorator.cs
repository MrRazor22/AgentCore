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

public class PersistentContextDecorator : IContextService
{
    private IContextService? _inner;
    private readonly string _filePath;
    private List<Message> _messages = new();

    public PersistentContextDecorator(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>
    /// Callback passed to AddContextLayer. Wraps the core context service.
    /// </summary>
    public IContextService Initialize(IContextService inner)
    {
        _inner = inner;
        return this;
    }

    public async Task<List<Message>> PrepareAsync(
        Message userInput,
        CancellationToken ct = default)
    {
        if (_inner == null)
        {
            var list = new List<Message>();
            list.AddRange(_messages);
            list.Add(userInput);
            return list;
        }
        return await _inner.PrepareAsync(userInput, ct);
    }

    public async Task UpdateAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        if (_inner != null)
        {
            await _inner.UpdateAsync(completedTurn, ct);
        }

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
