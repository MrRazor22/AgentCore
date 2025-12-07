using AgentCore.LLM.Client;
using AgentCore.LLM.Handlers;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Pipeline
{
    public interface ILLMPipeline
    {
        Task<LLMResponseBase> RunAsync(
            LLMRequestBase request,
            IChunkHandler handler,
            Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> streamFactory,
            Action<LLMStreamChunk>? onStream,
            CancellationToken ct);
    }
    public sealed class LLMPipeline : ILLMPipeline
    {
        private readonly IRetryPolicy _retryPolicy;
        private readonly IContextBudgetManager _ctxManager;
        private readonly ITokenManager _tokenManager;
        private readonly ILogger _logger;

        public LLMPipeline(
            IContextBudgetManager ctxManager,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger logger
            )
        {
            _retryPolicy = retryPolicy;
            _ctxManager = ctxManager;
            _tokenManager = tokenManager;
            _logger = logger;
        }
        public async Task<LLMResponseBase> RunAsync(
            LLMRequestBase request,
            IChunkHandler handler,
            Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> streamFactory,
            Action<LLMStreamChunk>? onStream,
            CancellationToken ct)
        {
            request.Prompt = _ctxManager.Trim(
                request.Prompt,
                request.Options?.MaxOutputTokens
            );

            handler.PrepareRequest(request);

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogDebug("► LLM Request:\n{Json}", request.ToPayloadString());

            LLMResponseBase response = null!;
            TokenUsage? usageReported = null;
            string finish = "stop";

            try
            {
                // Stream with retry protection - handler.OnChunk() can throw RetryException
                await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                    request.Prompt,
                    r => Iterate(),
                    ct))
                {
                    onStream?.Invoke(chunk);
                    handler.OnChunk(chunk);

                    if (chunk.Kind == StreamKind.Usage)
                        usageReported = chunk.AsTokenUsage() ?? new TokenUsage();

                    if (chunk.Kind == StreamKind.Finish && chunk.FinishReason != null)
                        finish = chunk.FinishReason;
                }

                async IAsyncEnumerable<LLMStreamChunk> Iterate()
                {
                    await foreach (var chunk in streamFactory(request))
                        yield return chunk;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                finish = "cancelled";
            }
            finally
            {
                // Build response content
                response = handler.BuildResponse(finish);

                if (_logger.IsEnabled(LogLevel.Trace))
                    _logger.LogDebug("► LLM Response:\n{Json}", response.ToPayloadString());

                // Resolve token usage
                if (usageReported != null)
                {
                    response.TokenUsage = usageReported;
                }
                else
                {
                    var inTok = _tokenManager.Count(request.ToPayloadString());
                    var outTok = _tokenManager.Count(response.ToPayloadString());
                    response.TokenUsage = new TokenUsage(inTok, outTok);
                }

                // Record for tracking
                _tokenManager.Record(response.TokenUsage);
            }

            return response;
        }
    }
}
