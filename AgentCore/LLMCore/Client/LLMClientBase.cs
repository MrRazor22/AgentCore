using AgentCore.Chat;
using AgentCore.LLMCore.Handlers;
using AgentCore.LLMCore.Pipeline;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLMCore.Client
{
    public abstract class LLMClientBase : ILLMClient
    {
        private readonly ILLMPipeline _pipeline;
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

            _pipeline = new LLMPipeline(
                _retryPolicy,
                _trimmer,
                _tokenizer,
                _tokenManager,
                _logger,
                _initOptions);
        }

        #region abstract methods providers must implement 
        protected abstract IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequestBase request,
            CancellationToken ct);
        #endregion 

        private async Task<TResponse> ExecuteWithHandlerAsync<TResponse>(
            LLMRequestBase request,
            IChunkHandler handler,
            Action<LLMStreamChunk>? onStream,
            CancellationToken ct)
        {
            handler.PrepareRequest(request);

            var result = await _pipeline.RunAsync(
            request,
            handler,
            r => StreamAsync(r, ct),
            onStream,
            ct);

            return (TResponse)result;
        }

        public async Task<LLMStructuredResponse<T>> ExecuteAsync<T>(
            LLMStructuredRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            return await ExecuteWithHandlerAsync<LLMStructuredResponse<T>>
                (request,
                new StructuredHandler<T>(request, _parser, _tools),
                onStream,
                ct);
        }

        public async Task<LLMResponse> ExecuteAsync(
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            return await ExecuteWithHandlerAsync<LLMResponse>
                (request,
                new TextToolCallHandler(_parser, _tools, request.Prompt),
                onStream,
                ct);
        }

    }
}
