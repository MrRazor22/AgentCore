using System.Runtime.CompilerServices;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.Layers.LLM;

public record RetryContext(
    Exception Exception,
    int Attempt,
    int MaxRetries,
    TimeSpan Delay);

public sealed class RetryOptions
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public double BackoffMultiplier { get; init; } = 2;
    public bool UseJitter { get; init; } = true;

    public Func<Exception, int, bool>? ShouldRetry { get; init; }
    public Action<RetryContext>? OnRetry { get; init; }
}

public sealed class RetryLayer : LLMLayer
{
    private readonly RetryOptions _options;
    private readonly Random _random = new();

    public RetryLayer(RetryOptions? options = null)
    {
        _options = options ?? new();
        ValidateOptions();
    }

    public override async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
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
                        attempt <= _options.MaxRetries &&
                        ShouldRetry(ex, attempt))
                    {
                        var delay = GetDelay(attempt);

                        _options.OnRetry?.Invoke(
                            new RetryContext(ex, attempt, _options.MaxRetries, delay));

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        break; // Retries by breaking to the outer attempt loop
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

    private bool ShouldRetry(Exception exception, int attempt) =>
        _options.ShouldRetry?.Invoke(exception, attempt)
        ?? IsTransient(exception);

    public static bool IsTransient(Exception exception)
    {
        if (exception is TimeoutException)
            return true;

        if (exception is HttpRequestException http)
        {
            var status = (int?)http.StatusCode;
            return status is null or 408 or 429 or >= 500;
        }

        return exception is IOException;
    }

    private TimeSpan GetDelay(int attempt)
    {
        var exponential = _options.InitialDelay.TotalMilliseconds *
                          Math.Pow(_options.BackoffMultiplier, attempt - 1);

        var delay = Math.Min(
            exponential,
            _options.MaxDelay.TotalMilliseconds);

        if (_options.UseJitter)
        {
            lock (_random)
                delay *= 0.5 + _random.NextDouble() * 0.5;
        }

        return TimeSpan.FromMilliseconds(delay);
    }

    private void ValidateOptions()
    {
        if (_options.MaxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(_options.MaxRetries));

        if (_options.InitialDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(_options.InitialDelay));

        if (_options.MaxDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(_options.MaxDelay));

        if (_options.BackoffMultiplier < 1 ||
            double.IsNaN(_options.BackoffMultiplier) ||
            double.IsInfinity(_options.BackoffMultiplier))
            throw new ArgumentOutOfRangeException(nameof(_options.BackoffMultiplier));
    }
}
