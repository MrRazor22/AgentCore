using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
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
        Task ExecuteAsync(
            Conversation originalRequest,
            Func<Conversation, Task> operation,
            CancellationToken ct = default);
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

        public async Task ExecuteAsync(
            Conversation originalRequest,
            Func<Conversation, Task> operation,
            CancellationToken ct = default)
        {
            var working = originalRequest.Clone();

            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var cloned = working.Clone();

                try
                {
                    await operation(cloned);
                    return; // Success!
                }
                catch (RetryException rex)
                {
                    if (attempt == _options.MaxRetries)
                        throw;

                    working.AddAssistant(rex.Message);
                    _logger.LogWarning(
                        "Retry {Attempt}: {Reason}",
                        attempt + 1,
                        rex.Message
                    );
                }
            }
        }
    }
}