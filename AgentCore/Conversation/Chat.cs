using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AgentCore.Tokens;

namespace AgentCore.Conversation;

public interface IShortTermMemory
{
    Task<List<Message>> RecallAsync(string sessionId);
    Task RetainAsync(string sessionId, List<Message> chat);
    Task ClearAsync(string sessionId);
}

public sealed class InMemoryShortTermMemory : IShortTermMemory
{
    private readonly ConcurrentDictionary<string, List<Message>> _store = new();
    private readonly ITokenCounter _tokenCounter;
    private readonly int? _contextLimit;
    private readonly double _threshold;

    public InMemoryShortTermMemory(ITokenCounter tokenCounter, int? contextLimit = null, double threshold = 0.75)
    {
        _tokenCounter = tokenCounter;
        _contextLimit = contextLimit;
        _threshold = threshold;
    }

    public Task<List<Message>> RecallAsync(string sessionId) =>
        Task.FromResult(_store.GetOrAdd(sessionId, _ => new List<Message>()));

    public async Task RetainAsync(string sessionId, List<Message> chat)
    {
        if (_contextLimit.HasValue && _contextLimit.Value > 0)
        {
            int totalTokens = await _tokenCounter.CountAsync(chat).ConfigureAwait(false);
            double usage = (double)totalTokens / _contextLimit.Value;
            if (usage >= _threshold)
            {
                int dropCount = Math.Max(1, chat.Count / 4);
                if (chat.Count > dropCount)
                {
                    chat.RemoveRange(0, dropCount);
                }
            }
        }
        _store[sessionId] = chat;
    }

    public Task ClearAsync(string sessionId)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
