using AgentCore.Chat;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLMCore
{
    public abstract class LLMClientBase : ILLMClient
    {
        private readonly IToolCatalog _tools;
        private readonly IToolCallParser _parser;
        private readonly ITokenizer _tokenizer;
        private readonly IContextTrimmer _trimmer;
        private readonly ITokenManager _tokenManager;
        private readonly IRetryPolicy _retryPolicy;
        private readonly ILogger<ILLMClient> _logger;
        protected LLMInitOptions _initOptions;

        public LLMClientBase(
            LLMInitOptions opts,
            IToolCatalog registry,
            IToolCallParser parser,
            ITokenizer tokenizer,
            IContextTrimmer trimmer,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger<ILLMClient> logger)
        {
            _initOptions = opts;
            _tools = registry;
            _parser = parser;
            _tokenizer = tokenizer;
            _trimmer = trimmer;
            _tokenManager = tokenManager;
            _retryPolicy = retryPolicy;
            _logger = logger;
        }

        #region abstract methods providers must implement 
        protected abstract IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequestBase request,
            CancellationToken ct);
        #endregion

        private async Task<object> RunPipelineAsync(
            LLMRequestBase request,
            IChunkHandler handler,
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
                r => StreamAsync(r, ct),
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
                    request.Model ?? _initOptions.Model
                );
            }

            var response = handler.BuildResponse(finish, inTok, outTok);

            if (outTok <= 0)
            {
                outTok = response switch
                {
                    LLMResponse r =>
                        _tokenizer.Count(r.AssistantMessage ?? "",
                            request.Model ?? _initOptions.Model),

                    LLMStructuredResponse<object> s =>
                        _tokenizer.Count(s.RawJson.ToString(),
                            request.Model ?? _initOptions.Model),

                    _ => outTok
                };
            }

            _tokenManager.Record(inTok, outTok);
            return response;
        }

        public async Task<LLMStructuredResponse<T>> ExecuteAsync<T>(
            LLMStructuredRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            var handler = new StructuredHandler<T>(request, _parser, _tools);

            handler.PrepareRequest(request);

            var result = await RunPipelineAsync(
                request,
                handler,
                onStream,
                ct);

            return (LLMStructuredResponse<T>)result;
        }



        public async Task<LLMResponse> ExecuteAsync(
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            var handler = new TextToolCallHandler(_parser, _tools, request.Prompt);

            handler.PrepareRequest(request);

            var result = await RunPipelineAsync(
                request,
                handler,
                onStream,
                ct);

            return (LLMResponse)result;
        }

    }
}
