using AgentCore.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AgentCore.LLM.Execution
{
    public class RetryException : Exception
    {
        public RetryException(string message) : base(message) { }
    }

    public sealed class EarlyStopException : Exception
    {
        public EarlyStopException(string message = "early-stop") : base(message) { }
    }

    public sealed class RetryPolicyOptions
    {
        public int MaxRetries { get; set; } = 3;
    }

    public interface IRetryPolicy
    {
        IAsyncEnumerable<T> ExecuteStreamingAsync<T>(
            Conversation originalRequest,
            Func<Conversation, IAsyncEnumerable<T>> operation,
            CancellationToken ct = default);
    }

    public sealed class RetryPolicy : IRetryPolicy
    {
        private readonly ILogger<RetryPolicy> _logger;
        private readonly RetryPolicyOptions _options;

        public RetryPolicy(ILogger<RetryPolicy> logger, IOptions<RetryPolicyOptions>? options = null)
        {
            _options = options?.Value ?? new RetryPolicyOptions();
            _logger = logger;
        }

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

                await foreach (var result in CaptureRetryException(operation(cloned), ct))
                {
                    if (result.IsSuccess)
                        yield return result.Value!;
                    else
                        retryEx = result.Error;
                }

                if (retryEx == null)
                    yield break;

                if (attempt == _options.MaxRetries)
                    throw retryEx;

                working.AddAssistant(retryEx.Message);
                _logger.LogWarning("Retry {Attempt}: {Reason}", attempt + 1, retryEx.Message);
            }
        }

        private static async IAsyncEnumerable<RetryResult<T>> CaptureRetryException<T>(
            IAsyncEnumerable<T> source,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await using var enumerator = source.GetAsyncEnumerator(ct);
            RetryException? caughtException = null;

            while (caughtException == null)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (RetryException ex)
                {
                    caughtException = ex;
                    break;
                }

                if (!moved)
                    break;

                yield return RetryResult<T>.FromValue(enumerator.Current);
            }

            if (caughtException != null)
                yield return RetryResult<T>.FromError(caughtException);
        }

        private readonly struct RetryResult<T>
        {
            public bool IsSuccess { get; }
            public T? Value { get; }
            public RetryException? Error { get; }

            private RetryResult(bool isSuccess, T? value, RetryException? error)
            {
                IsSuccess = isSuccess;
                Value = value;
                Error = error;
            }

            public static RetryResult<T> FromValue(T value) => new(true, value, null);
            public static RetryResult<T> FromError(RetryException error) => new(false, default, error);
        }
    }
}
