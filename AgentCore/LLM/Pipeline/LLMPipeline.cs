using AgentCore.Chat;
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
    public sealed class EarlyStopException : Exception
    {
        public EarlyStopException(string message = "early-stop") : base(message) { }
    }
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
        private readonly ILogger<LLMPipeline> _logger;

        public LLMPipeline(
            IContextBudgetManager ctxManager,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger<LLMPipeline> logger
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ApplyTrim(request.Prompt, request);

            handler.PrepareRequest(request);

            _logger.LogTrace("► LLM Request [Payload]:\n{Json}", request.ToPayloadJson().AsPrettyJson());


            LLMResponseBase response = null!;
            TokenUsage? usageReported = null;
            FinishReason finish = FinishReason.Stop;

            try
            {
                async IAsyncEnumerable<LLMStreamChunk> IterateChunk(Conversation retryPrompt)
                {
                    ApplyTrim(retryPrompt, request);
                    request.Prompt = retryPrompt;
                    await foreach (var chunk in streamFactory(request))
                    {
                        onStream?.Invoke(chunk);
                        handler.HandleChunk(chunk);
                        yield return chunk;
                    }
                }

                await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                    request.Prompt,
                    retryPrompt => IterateChunk(retryPrompt),
                    ct))
                {
                    if (chunk.Kind == StreamKind.Usage)
                        usageReported = chunk.AsTokenUsage() ?? new TokenUsage();

                    if (chunk.Kind == StreamKind.Finish)
                        finish = chunk.AsFinishReason() ?? FinishReason.Stop;
                }
            }
            catch (EarlyStopException ese)
            {
                _logger.LogWarning("► EarlyStopException:\n{ese}", ese.Message);
            }
            catch (OperationCanceledException oce) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("► OperationCanceledException:\n{ese}", oce);
                finish = FinishReason.Cancelled;
            }
            finally
            {
                response = handler.BuildResponse(finish);

                sw.Stop();
                _logger.LogInformation("► Call Duration: {ms} ms", sw.ElapsedMilliseconds);

                _logger.LogTrace("► LLM Response [Payload]:\n{Json}", response.ToPayloadJson().AsPrettyJson());

                response.TokenUsage = _tokenManager.ResolveAndRecord(
                    request.ToPayloadJson().ToString(),
                    response.ToPayloadJson().ToString(),
                    usageReported
                );
            }

            return response;
        }
        private void ApplyTrim(Conversation convo, LLMRequestBase request)
        {
            request.Prompt = _ctxManager.Trim(
                convo,
                request.Options?.MaxOutputTokens
            );
        }
    }
}
