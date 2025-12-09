using AgentCore.Chat;
using AgentCore.LLM.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Pipeline
{
    public class RetryException : Exception
    {
        public RetryException(string message) : base(message) { }
    }
    public sealed class RetryPolicyOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public double BackoffFactor { get; set; } = 2.0;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);
        public bool Enabled { get; set; } = true;
    }

    public interface IRetryPolicy
    {
        IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
            Conversation originalRequest,
            Func<Conversation, IAsyncEnumerable<LLMStreamChunk>> factory,
            [EnumeratorCancellation] CancellationToken ct = default);
    }

    /// <summary>
    /// Default policy, preserves existing retry loops (JSON + Tool calls).
    /// </summary>
    public sealed class RetryPolicy : IRetryPolicy
    {
        private readonly ILogger<RetryPolicy> _logger;
        private readonly RetryPolicyOptions _options;

        public RetryPolicy(ILogger<RetryPolicy> logger, IOptions<RetryPolicyOptions>? options = null)
        {
            _options = options?.Value ?? new RetryPolicyOptions();
            _logger = logger;
        }

        public async IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
            Conversation originalRequest,
            Func<Conversation, IAsyncEnumerable<LLMStreamChunk>> streamFactory,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var workingRequest = originalRequest.Clone();

            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                using var timeoutCts = new CancellationTokenSource(_options.Timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var cloned = workingRequest.Clone();
                var stream = streamFactory(cloned);
                var enumerator = stream.GetAsyncEnumerator(linked.Token);

                bool retry = false;
                string? reason = null;

                try
                {
                    while (true)
                    {
                        bool moved;

                        try { moved = await enumerator.MoveNextAsync(); }
                        catch (RetryException rex)
                        {
                            retry = true;
                            reason = rex.Message;
                            break;
                        }

                        if (!moved)
                            break;

                        yield return enumerator.Current;
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }

                if (!retry)
                    yield break;

                if (attempt == _options.MaxRetries)
                    yield break;

                // teach the model what to fix
                workingRequest.AddAssistant(reason);
                _logger.LogWarning(
                    "Retry {Attempt}: reason = {Reason}",
                    attempt + 1,
                    reason
                );
                // artificial retry chunk for visibility
                var delay = TimeSpan.FromMilliseconds(
                    _options.InitialDelay.TotalMilliseconds *
                    Math.Pow(_options.BackoffFactor, attempt)
                );

                _logger.LogWarning("Waiting {Delay} before next attempt", delay);

                await Task.Delay(delay, ct);
            }
        }
    }
}
