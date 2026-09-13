using System.Runtime.CompilerServices;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.Layers.LLM;

public sealed class RetryLayer : LLMLayer
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _backoffMultiplier;
    private readonly bool _useJitter;
    private readonly Func<Exception, int, bool>? _shouldRetry;
    private readonly Action<Exception, int, TimeSpan>? _onRetry;

    public RetryLayer(
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfLessThan(backoffMultiplier, 1.0);
        if (double.IsNaN(backoffMultiplier) || double.IsInfinity(backoffMultiplier))
            throw new ArgumentOutOfRangeException(nameof(backoffMultiplier));

        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);

        if (_initialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initialDelay));
        if (_maxDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxDelay));

        _backoffMultiplier = backoffMultiplier;
        _useJitter = useJitter;
        _shouldRetry = shouldRetry;
        _onRetry = onRetry;
    }

    public override async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var attempt = 1;
        while (true)
        {
            var yielded = false;
            IAsyncEnumerator<IMessageEvent>? enumerator = null;

            try
            {
                enumerator = Inner.StreamAsync(messages, responseSchema, tools, ct)
                                  .GetAsyncEnumerator(ct);

                while (true)
                {
                    bool hasNext;
                    IMessageEvent? item = null;

                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        if (hasNext)
                        {
                            item = enumerator.Current;
                        }
                    }
                    catch (Exception ex) when (
                        !yielded &&
                        attempt <= _maxRetries &&
                        (_shouldRetry?.Invoke(ex, attempt) ?? IsTransient(ex)))
                    {
                        var delay = GetDelay(attempt);
                        _onRetry?.Invoke(ex, attempt, delay);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        attempt++;
                        break;
                    }

                    if (!hasNext)
                    {
                        yield break;
                    }

                    yielded = true;
                    yield return item!;
                }
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public static bool IsTransient(Exception exception)
    {
        if (exception is TimeoutException or IOException)
            return true;

        if (exception is HttpRequestException http)
        {
            var status = (int?)http.StatusCode;
            return status is null or 408 or 429 or >= 500;
        }

        return false;
    }

    private TimeSpan GetDelay(int attempt)
    {
        var exponential = _initialDelay.TotalMilliseconds *
                          Math.Pow(_backoffMultiplier, attempt - 1);

        var delay = Math.Min(exponential, _maxDelay.TotalMilliseconds);

        if (_useJitter)
        {
            delay *= 0.5 + Random.Shared.NextDouble() * 0.5;
        }

        return TimeSpan.FromMilliseconds(delay);
    }
}
