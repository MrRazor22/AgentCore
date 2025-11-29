using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
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
                request,
                request.Options?.MaxOutputTokens
            );

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogDebug("► Outbound Messages:\n{Json}", request.Prompt.ToJson());

            TokenUsage tokenUsage = TokenUsage.Empty;
            string finish = "stop";

            var liveLog = new StringBuilder();
            LLMResponseBase? response;

            try
            {
                await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                    request,
                    r => Iterate(),
                    ct))
                {
                    onStream?.Invoke(chunk);
                    handler.OnChunk(chunk);

                    if (chunk.Kind == StreamKind.Usage)
                        tokenUsage = chunk.AsTokenUsage() ?? TokenUsage.Empty;

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
                // 1) First let handler build the response WITHOUT token usage
                response = handler.BuildResponse(finish, TokenUsage.Empty);

                // 2) Now compute tokens using actual serialized payload
                int inTokens = tokenUsage.InputTokens;
                int outTokens = tokenUsage.OutputTokens;

                if (inTokens <= 0)
                    inTokens = _tokenManager.Count(request.ToSerializablePayload());

                if (outTokens <= 0)
                    outTokens = _tokenManager.Count(response.ToSerializablePayload());

                var final = new TokenUsage(inTokens, outTokens);

                // 3) Now assign real usage into the response
                response.TokenUsage = final;

                // 5) Record usage
                _tokenManager.Record(final);
            }

            return response;
        }
    }
}
