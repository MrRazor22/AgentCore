using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCoreT04;

public record CacheEntry(DateTimeOffset Timestamp, IReadOnlyList<IMessageEvent> Events);

public sealed class CacheLayer(
    ILLM? inner = null,
    TimeSpan? ttl = null,
    ConcurrentDictionary<string, CacheEntry>? cacheStore = null) : LLMLayer(inner)
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = cacheStore ?? new();

    public int CacheHitCount { get; private set; }
    public int CacheMissCount { get; private set; }

    public static string DeriveCacheKey(IReadOnlyList<Message> messages, IReadOnlyList<ToolDefinition>? tools = null)
    {
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            sb.Append((int)m.Role).Append(':');
            foreach (var c in m.Contents)
            {
                if (c is Text t) sb.Append(t.Value);
                else sb.Append(c);
            }
            sb.Append(';');
        }

        if (tools is { Count: > 0 })
        {
            sb.Append("#TOOLS:");
            foreach (var tool in tools)
            {
                sb.Append(tool.Name).Append(':').Append(tool.Description).Append(';');
            }
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var key = DeriveCacheKey(messages, tools);

        if (_cache.TryGetValue(key, out var entry) && (DateTimeOffset.UtcNow - entry.Timestamp) < _ttl)
        {
            CacheHitCount++;
            foreach (var evt in entry.Events)
            {
                yield return evt;
            }
            yield break;
        }

        CacheMissCount++;
        var recordedEvents = new List<IMessageEvent>();

        await foreach (var evt in Inner.GenerateAsync(messages, tools, responseSchema, ct).ConfigureAwait(false))
        {
            recordedEvents.Add(evt);
            yield return evt;
        }

        _cache[key] = new CacheEntry(DateTimeOffset.UtcNow, recordedEvents);
    }
}
