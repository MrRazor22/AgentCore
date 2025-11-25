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
        private readonly IContextTrimmer _trimmer;
        private readonly ITokenizer _tokenizer;
        private readonly ITokenManager _tokenManager;
        private readonly ILogger _logger;
        private readonly LLMInitOptions _opts;

        public LLMPipeline(
            IRetryPolicy retryPolicy,
            IContextTrimmer trimmer,
            ITokenizer tokenizer,
            ITokenManager tokenManager,
            ILogger logger,
            LLMInitOptions opts)
        {
            _retryPolicy = retryPolicy;
            _trimmer = trimmer;
            _tokenizer = tokenizer;
            _tokenManager = tokenManager;
            _logger = logger;
            _opts = opts;
        }

        public async Task<object> RunAsync(
            LLMRequestBase request,
            IChunkHandler handler,
            Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> streamFactory,
            Action<LLMStreamChunk>? onStream,
            CancellationToken ct)
        {
            // ---- TRIM CONTEXT FIRST ----
            request.Prompt = _trimmer.Trim(
                request.Prompt,
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

            // ---- FALLBACK TOKEN COUNTING ----
            if (inTok <= 0)
            {
                inTok = _tokenizer.Count(
                    request.Prompt.ToJson(ChatFilter.All),
                    request.Model ?? _opts.Model
                );
            }

            var response = handler.BuildResponse(finish, inTok, outTok);

            // ---- FALLBACK OUTPUT TOKENS ----
            if (outTok <= 0)
            {
                var text = handler.GetOutputTextForTokenCount(response);
                outTok = _tokenizer.Count(text, request.Model ?? _opts.Model);
            }

            _tokenManager.Record(inTok, outTok);
            return response;
        }
    }

}
