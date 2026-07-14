using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.Memory;

namespace AgentCore.Example;

public class PersistentMemoryLayer : IMemoryService
{
    private IMemoryService? _inner;
    private readonly string _filePath;
    private List<Message> _messages = new();

    public PersistentMemoryLayer(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>
    /// Callback passed to AddMemoryLayer. Wraps the core memory service.
    /// </summary>
    public IMemoryService Initialize(IMemoryService inner)
    {
        _inner = inner;
        return this;
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        if (_inner != null)
        {
            await _inner.RememberAsync(completedTurn, ct);
        }

        _messages.AddRange(completedTurn);

        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    public Task<IReadOnlyList<Message>> RecallAsync(Message currentInput, int? maxTokens, CancellationToken ct = default)
    {
        if (_inner == null)
        {
            return Task.FromResult<IReadOnlyList<Message>>(_messages);
        }
        return _inner.RecallAsync(currentInput, maxTokens, ct);
    }

    public IReadOnlyList<Message> GetLocalMessages() => _messages;

    public void SetLocalMessages(List<Message> messages)
    {
        _messages = messages.ToList();
    }
}
