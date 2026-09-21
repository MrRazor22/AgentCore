using System.Diagnostics;
using System.Runtime.CompilerServices;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCoreT05;

public class RateLimitExceededException(string message) : Exception(message);

public sealed class TokenBucket
{
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private double _tokens;
    private long _lastRefillTicks;
    private readonly object _lock = new();

    public TokenBucket(double capacity, double refillPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _tokens = capacity;
        _lastRefillTicks = Stopwatch.GetTimestamp();
    }

    public double AvailableTokens
    {
        get
        {
            lock (_lock)
            {
                RefillLocked();
                return _tokens;
            }
        }
    }

    public bool TryConsume(double count = 1.0)
    {
        lock (_lock)
        {
            RefillLocked();
            if (_tokens >= count)
            {
                _tokens -= count;
                return true;
            }
            return false;
        }
    }

    public async Task<bool> ConsumeAsync(double count = 1.0, TimeSpan maxWait = default, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delayNeeded;
            lock (_lock)
            {
                RefillLocked();
                if (_tokens >= count)
                {
                    _tokens -= count;
                    return true;
                }

                var deficit = count - _tokens;
                var secondsNeeded = deficit / _refillPerSecond;
                delayNeeded = TimeSpan.FromSeconds(secondsNeeded);

                if (maxWait != TimeSpan.Zero && delayNeeded > maxWait)
                {
                    return false;
                }
            }

            if (delayNeeded > TimeSpan.Zero)
            {
                await Task.Delay(delayNeeded, ct).ConfigureAwait(false);
            }
        }
        return false;
    }

    private void RefillLocked()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = (now - _lastRefillTicks) / (double)Stopwatch.Frequency;
        _lastRefillTicks = now;

        _tokens = Math.Min(_capacity, _tokens + (elapsedSeconds * _refillPerSecond));
    }
}

public sealed class RateLimitLayer(
    TokenBucket bucket,
    bool blocking = true,
    TimeSpan maxWait = default,
    ILLM? inner = null) : LLMLayer(inner)
{
    private readonly TokenBucket _bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
    private readonly bool _blocking = blocking;
    private readonly TimeSpan _maxWait = maxWait;

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_blocking)
        {
            if (!_bucket.TryConsume(1.0))
            {
                throw new RateLimitExceededException("Rate limit burst capacity exceeded (non-blocking).");
            }
        }
        else
        {
            var consumed = await _bucket.ConsumeAsync(1.0, _maxWait, ct).ConfigureAwait(false);
            if (!consumed)
            {
                throw new RateLimitExceededException($"Rate limit exceeded: queue wait time exceeds {_maxWait.TotalMilliseconds}ms.");
            }
        }

        await foreach (var evt in Inner.GenerateAsync(messages, tools, responseSchema, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
