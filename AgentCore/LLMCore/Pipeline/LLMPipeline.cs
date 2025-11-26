using AgentCore.Chat;
using AgentCore.LLMCore.Client;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLMCore.Pipeline
{
    public interface ILLMPipeline
    {
        Task<object> RunAsync(
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
        private readonly ITokenEstimator _tokenEstimator;
        private readonly ILogger _logger;

        public LLMPipeline(
            ITokenEstimator tokenEstimator,
            IContextBudgetManager ctxManager,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger logger
            )
        {
            _retryPolicy = retryPolicy;
            _ctxManager = ctxManager;
            _tokenManager = tokenManager;
            _tokenEstimator = tokenEstimator;
            _logger = logger;
        }

        public async Task<object> RunAsync(
            LLMRequestBase request,
            IChunkHandler handler,
            Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> streamFactory,
            Action<LLMStreamChunk>? onStream,
            CancellationToken ct)
        {
            // ---- TRIM CONTEXT FIRST ----
            request.Prompt = _ctxManager.Trim(
                request,
                request.Options?.MaxOutputTokens,
                request.Model
            );

            int inTok = 0;
            int outTok = 0;
            string finish = "stop";

            var liveLog = new StringBuilder();

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("► Outbound Messages:\n{Json}",
                    JsonConvert.SerializeObject(request.Prompt.ToLogList(), Formatting.Indented));
            }

            // ---- SINGLE STREAM LOOP ----
            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                request,
                streamFactory,
                ct))
            {
                switch (chunk.Kind)
                {
                    case StreamKind.Text:
                        var txt = chunk.AsText() ?? "";
                        liveLog.Append(txt);

                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("◄ Inbound Stream: {Text}", liveLog.ToString());
                        break;

                    case StreamKind.ToolCallDelta:
                        var td = chunk.AsToolCallDelta();
                        liveLog.Append(td.Delta);

                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("◄ [{Name}] Args: {Args}",
                                td.Name, liveLog.ToString());
                        break;
                }

                if (chunk.InputTokens.HasValue)
                    inTok = chunk.InputTokens.Value;

                if (chunk.OutputTokens.HasValue)
                    outTok = chunk.OutputTokens.Value;

                if (chunk.Kind == StreamKind.Finish && chunk.FinishReason != null)
                    finish = chunk.FinishReason;

                onStream?.Invoke(chunk);
                handler.OnChunk(chunk);
            }

            var actualIn = inTok;
            var actualOut = outTok;

            var debug = _logger.IsEnabled(LogLevel.Debug);

            // compute estimation only when needed OR debug wants visibility
            var estIn = (actualIn <= 0 || debug)
                ? _tokenEstimator.Estimate(request)
                : 0;

            var resolvedIn = actualIn > 0 ? actualIn : estIn;

            var response = (LLMResponseBase)handler.BuildResponse(finish, resolvedIn, actualOut);

            var estOut = (actualOut <= 0 || debug)
                ? _tokenEstimator.Estimate(response, request.Model)
                : 0;

            var resolvedOut = actualOut > 0 ? actualOut : estOut;

            _tokenManager.Record(resolvedIn, resolvedOut);

            // normal logging: only resolved usage
            _logger.LogInformation($"Tokens In={resolvedIn} | Out={resolvedOut}");

            // debug logging: add estimation
            if (debug) _logger.LogDebug($"Estimation In={estIn} | Out={estOut}");

            return response;
        }
    }

}
