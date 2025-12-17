using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Client
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
        IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
            Conversation originalRequest,
            Func<Conversation, IAsyncEnumerable<LLMStreamChunk>> factory,
            [EnumeratorCancellation] CancellationToken ct = default);
    }

    /// <summary>
    /// Retry policy for handling high-level LLM errors (tool validation, JSON parsing). 
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
             Func<Conversation, IAsyncEnumerable<LLMStreamChunk>> factory,
             [EnumeratorCancellation] CancellationToken ct = default)
        {
            var working = originalRequest.Clone();
            RetryException? lastRetry = null;

            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var cloned = working.Clone();
                IAsyncEnumerable<LLMStreamChunk> stream;

                try
                {
                    stream = factory(cloned);
                }
                catch (RetryException rex)
                {
                    lastRetry = rex;
                    goto Retry;
                }

                await foreach (var chunk in Consume(stream, ct))
                    yield return chunk;

                yield break;

            Retry:
                if (attempt == _options.MaxRetries)
                    throw lastRetry!;

                working.AddAssistant(lastRetry!.Message);
                _logger.LogWarning(
                    "Retry {Attempt}: {Reason}",
                    attempt + 1,
                    lastRetry!.Message
                );
            }
        }

        private static async IAsyncEnumerable<LLMStreamChunk> Consume(
            IAsyncEnumerable<LLMStreamChunk> stream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var c in stream.WithCancellation(ct))
                yield return c;
        }
    }
}