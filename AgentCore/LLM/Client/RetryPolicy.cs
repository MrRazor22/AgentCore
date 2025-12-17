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
            Func<Conversation, IAsyncEnumerable<LLMStreamChunk>> streamFactory,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var workingRequest = originalRequest.Clone();

            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                var cloned = workingRequest.Clone();
                var stream = streamFactory(cloned);
                var enumerator = stream.GetAsyncEnumerator(ct);

                bool retry = false;
                string? reason = null;

                try
                {
                    while (true)
                    {
                        bool moved;
                        try
                        {
                            moved = await enumerator.MoveNextAsync();
                        }
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

                if (!retry || attempt == _options.MaxRetries)
                    yield break;

                workingRequest.AddAssistant(reason);
                _logger.LogWarning("Retry {Attempt}: {Reason}", attempt + 1, reason);
            }
        }
    }
}