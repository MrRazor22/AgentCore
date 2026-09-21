using System.Net;
using System.Runtime.CompilerServices;
using AgentCore;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.LLM;

public class RetryLayer(
    ILLM? inner = null,
    int maxRetries = 3,
    TimeSpan? initialDelay = null,
    double jitterRatio = 0.0,
    Func<Exception, bool>? isTransient = null,
    Func<TimeSpan, CancellationToken, Task>? sleep = null) : LLMLayer(inner)
{
    public int MaxRetries { get; } = maxRetries;
    public TimeSpan InitialDelay { get; } = initialDelay ?? TimeSpan.FromMilliseconds(100);
    public double JitterRatio { get; } = jitterRatio;

    private readonly Func<Exception, bool> _isTransient = isTransient ?? DefaultIsTransient;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep = sleep ?? Task.Delay;

    public static bool DefaultIsTransient(Exception ex)
    {
        if (ex is AggregateException agg && agg.InnerExceptions.Count == 1)
            ex = agg.InnerExceptions[0];

        return ex switch
        {
            HttpRequestException hre => hre.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable,
            _ => false
        };
    }

    public static TimeSpan CalculateDelay(int attempt, TimeSpan initialDelay, double jitterRatio = 0.0, Random? random = null)
    {
        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be at least 1.");

        var factor = Math.Pow(2, attempt - 1);
        var baseMs = initialDelay.TotalMilliseconds * factor;

        if (jitterRatio <= 0.0)
            return TimeSpan.FromMilliseconds(baseMs);

        var rng = random ?? Random.Shared;
        var jitter = rng.NextDouble() * jitterRatio * baseMs;
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            var yieldedAny = false;
            var retryRequired = false;

            IAsyncEnumerator<IMessageEvent>? enumerator = null;
            try
            {
                enumerator = base.GenerateAsync(messages, tools, responseSchema, ct).GetAsyncEnumerator(ct);

                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!yieldedAny && attempt <= MaxRetries && _isTransient(ex))
                    {
                        retryRequired = true;
                        break;
                    }

                    if (!hasNext)
                        yield break;

                    yieldedAny = true;
                    yield return enumerator.Current;
                }
            }
            finally
            {
                if (enumerator != null)
                    await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            if (retryRequired)
            {
                var delay = CalculateDelay(attempt, InitialDelay, JitterRatio);
                await _sleep(delay, ct).ConfigureAwait(false);
            }
        }
    }
}
