using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.Conversation;

public sealed class ChatInMemoryStore : IChatMemory
{
    private readonly ConcurrentDictionary<string, List<Message>> _store = new();

    public Task<IReadOnlyList<string>> GetAllSessionsAsync() =>
        Task.FromResult<IReadOnlyList<string>>(_store.Keys.ToList());

    public Task<List<Message>> RecallAsync(string sessionId) =>
        Task.FromResult(_store.GetOrAdd(sessionId, _ => new List<Message>()));

    public Task RetainAsync(string sessionId, List<Message> chat)
    {
        _store[sessionId] = chat;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stores and retrieves conversation history (List&lt;Message&gt;) per session.
/// This is the chat memory layer — separate from IMemory (cognitive/knowledge memory).
/// </summary>
public interface IChatMemory
{
    Task<IReadOnlyList<string>> GetAllSessionsAsync();
    Task<List<Message>> RecallAsync(string sessionId);
    Task RetainAsync(string sessionId, List<Message> chat);
    Task ClearAsync(string sessionId);
}



