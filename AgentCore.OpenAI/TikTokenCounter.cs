using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using SharpToken;
using System.Runtime.CompilerServices;

namespace AgentCore.Providers.OpenAI;

public sealed class TikTokenCounter(string encodingName) : ITokenCounter
{
    private readonly GptEncoding _encoding = GptEncoding.GetEncoding(encodingName);
    private static readonly ConditionalWeakTable<Message, IntBox> _cache = new();
    private sealed class IntBox(int count) { public int Count = count; }

    public Task<int> CountAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        if (messages == null || !messages.Any()) return Task.FromResult(0);
        
        int total = 0;
        foreach (var m in messages)
        {
            if (_cache.TryGetValue(m, out var box))
            {
                total += box.Count;
            }
            else
            {
                int c = _encoding.Encode(new[] { m }.ToJson()).Count;
                _cache.Add(m, new IntBox(c));
                total += c;
            }
        }
        return Task.FromResult(total);
    }
}
