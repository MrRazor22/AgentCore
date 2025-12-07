using AgentCore.Json;
using AgentCore.LLM.Client;
using AgentCore.LLM.Handlers;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

            _logger.LogTrace("► LLM Request [Payload]:\n{Json}", request.ToPayloadJson().AsPrettyJson());


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
                    handler.HandleChunk(chunk);

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

                _logger.LogTrace("► LLM Response [Payload]:\n{Json}", response.ToPayloadJson().AsPrettyJson());
                // Resolve token usage
                if (usageReported != null)
                {
                    response.TokenUsage = usageReported;
                }
                else
                {
                    _logger.LogDebug("Tokens Approximated!");
                    var inTok = _tokenManager.Count(request.ToPayloadJson().ToString());
                    var outTok = _tokenManager.Count(response.ToPayloadJson().ToString());
                    response.TokenUsage = new TokenUsage(inTok, outTok);
                }

                // Record for tracking
                _tokenManager.Record(response.TokenUsage);
            }

            return response;
        }
    }
}
