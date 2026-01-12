using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Providers;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Execution
{
    public interface ILLMExecutor
    {
        Task<LLMResponse> ExecuteAsync(
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null);
    }

    public class LLMExecutor : ILLMExecutor
    {
        private readonly ILLMStreamProvider _provider;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IReadOnlyList<IChunkHandler> _handlers;
        private readonly IContextManager _ctxManager;
        private readonly ILogger<LLMExecutor> _logger;

        public LLMExecutor(
            ILLMStreamProvider provider,
            IRetryPolicy retryPolicy,
            IEnumerable<IChunkHandler> handlers,
            IContextManager ctxManager,
            ILogger<LLMExecutor> logger)
        {
            _provider = provider;
            _retryPolicy = retryPolicy;
            _handlers = handlers.ToList();
            _ctxManager = ctxManager;
            _logger = logger;
        }

        public async Task<LLMResponse> ExecuteAsync(
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            var response = new LLMResponse();
            var sw = Stopwatch.StartNew();

            var initialPrompt = _ctxManager.Trim(
                request.Prompt,
                request.Options?.MaxOutputTokens);

            async Task ExecuteAttempt(Conversation retryPrompt)
            {
                var trimmed = _ctxManager.Trim(
                    retryPrompt,
                    request.Options?.MaxOutputTokens);

                var attempt = request.Clone();
                attempt.Prompt = trimmed;

                foreach (var h in _handlers)
                    h.OnRequest(attempt);

                _logger.LogTrace(
                    "LLM request: {Request}",
                    attempt.ToCountablePayload());

                try
                {
                    await foreach (var chunk in _provider.StreamAsync(attempt, ct))
                    {
                        onStream?.Invoke(chunk);

                        foreach (var h in _handlers)
                            if (h.Kind == chunk.Kind)
                                h.OnChunk(chunk);
                    }
                }
                catch (EarlyStopException e)
                {
                    _logger.LogDebug("Early stop: {Msg}", e.Message);
                }

                // Validate after streaming completes
                foreach (var h in _handlers)
                    h.OnResponse(response); // Throws RetryException if validation fails
            }

            try
            {
                await _retryPolicy.ExecuteAsync(initialPrompt, ExecuteAttempt, ct);
            }
            catch (OperationCanceledException e) when (ct.IsCancellationRequested)
            {
                response.FinishReason = FinishReason.Cancelled;
                _logger.LogWarning("Cancellation Requested: {Msg}", e.Message);
            }
            finally
            {
                sw.Stop();

                _logger.LogTrace(
                    "LLM response: {Response}",
                    response.ToCountablePayload()
                );
                _logger.LogDebug(
                     "LLM call Duration={Ms}ms FinishReason={FinishReason}",
                     sw.ElapsedMilliseconds,
                     response.FinishReason
                 );
            }
            return response;
        }
    }
}