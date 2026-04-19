using AgentCore.Conversation;
using AgentCore.Json;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace AgentCore.Tokens;

public interface ITokenCounter
{
    Task<int> CountAsync(IEnumerable<Message> messages, CancellationToken ct = default);
}
public sealed class ApproximateTokenCounter : ITokenCounter
{
    private double _charsPerToken = 5.0;
    private int _sampleCount = 0;
    private readonly object _lock = new();
    private readonly ILogger<ApproximateTokenCounter> _logger;

    public ApproximateTokenCounter(ILogger<ApproximateTokenCounter>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ApproximateTokenCounter>.Instance;
    }

    private static readonly ConditionalWeakTable<Message, CharCountBox> _cache = new();
    private sealed class CharCountBox(int count) { public int Count = count; }

    public Task<int> CountAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        if (messages == null || !messages.Any()) return Task.FromResult(0);

        int charCount = GetCharCount(messages);

        double cpt;
        lock (_lock) cpt = _charsPerToken;

        int estimated = (int)(charCount / cpt);

        // Add 15% safety margin to approximations
        int padded = (int)(estimated * 1.15);

        _logger.LogDebug("Token count: MessageCount={MsgCount} CharCount={CharCount} EstimatedTokens={Est} PaddedTokens={Pad} CharsPerToken={CPT}",
            messages.Count(), charCount, estimated, padded, cpt);

        return Task.FromResult(padded);
    }

    public void Calibrate(IEnumerable<Message> messages, int actualTokenCount)
    {
        if (messages == null || actualTokenCount <= 0) return;

        int charCount = GetCharCount(messages);
        if (charCount <= 0) return;

        double currentRatio = (double)charCount / actualTokenCount;

        // Prevent wild outliers from destroying the ratio
        if (currentRatio < 1.0 || currentRatio > 10.0)
        {
            _logger.LogWarning("Token calibration rejected: CharCount={Char} TokenCount={Tokens} Ratio={Ratio} (out of range 1.0-10.0)",
                charCount, actualTokenCount, currentRatio);
            return;
        }

        lock (_lock)
        {
            double oldRatio = _charsPerToken;
            // Moving average of the last N samples to gently adjust
            _charsPerToken = ((_charsPerToken * _sampleCount) + currentRatio) / (_sampleCount + 1);
            if (_sampleCount < 100) _sampleCount++; // Cap weight to prevent lock-in
            _logger.LogDebug("Token calibration: OldRatio={Old} NewRatio={New} SampleCount={Sample}",
                oldRatio, _charsPerToken, _sampleCount);
        }
    }

    private static int GetCharCount(IEnumerable<Message> messages)
    {
        int total = 0;
        foreach (var m in messages)
        {
            if (_cache.TryGetValue(m, out var box))
            {
                total += box.Count;
            }
            else
            {
                int c = 4; // overhead
                foreach (var content in m.Contents)
                {
                    if (content is Text t) c += t.Value?.Length ?? 0;
                    else if (content is Reasoning) c += 0; // reasoning doesn't count toward token estimates
                    else if (content is ToolCall tc) c += tc.Name.Length + (tc.Arguments?.ToString()?.Length ?? 0);
                    else if (content is ToolResult tr) c += tr.Result?.ForLlm()?.Length ?? 0;
                }
                
                _cache.Add(m, new CharCountBox(c));
                total += c;
            }
        }
        return total;
    }
}
