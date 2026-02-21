using AgentCore.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace AgentCore.LLM.Execution;

public class RetryException(string message) : Exception(message) { }

public sealed class EarlyStopException(string message = "early-stop") : Exception(message) { }

public sealed class RetryPolicyOptions { public int MaxRetries { get; set; } = 3; }

public interface IRetryPolicy
{
    IAsyncEnumerable<T> ExecuteStreamingAsync<T>(
        Conversation originalRequest,
        Func<Conversation, IAsyncEnumerable<T>> operation,
        CancellationToken ct = default);
}

public sealed class RetryPolicy(ILogger<RetryPolicy> logger, IOptions<RetryPolicyOptions>? options = null) : IRetryPolicy
{
    private readonly RetryPolicyOptions _options = options?.Value ?? new RetryPolicyOptions();

    public async IAsyncEnumerable<T> ExecuteStreamingAsync<T>(
        Conversation originalRequest,
        Func<Conversation, IAsyncEnumerable<T>> operation,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var working = originalRequest.Clone();

        for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var cloned = working.Clone();
            RetryException? retryEx = null;

            await using var e = operation(cloned).GetAsyncEnumerator(ct);
            while (retryEx == null)
            {
                try { if (!await e.MoveNextAsync()) break; }
                catch (RetryException ex) { retryEx = ex; break; }
                yield return e.Current;
            }

            if (retryEx == null) yield break;
            if (attempt == _options.MaxRetries) throw retryEx;

            working.AddAssistant(retryEx.Message);
            logger.LogWarning("Retry {Attempt}: {Reason}", attempt + 1, retryEx.Message);
        }
    }
}
