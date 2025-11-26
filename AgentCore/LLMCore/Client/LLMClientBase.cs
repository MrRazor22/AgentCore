using AgentCore.LLMCore.Handlers;
using AgentCore.LLMCore.Pipeline;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLMCore.Client
{
    public abstract class LLMClientBase : ILLMClient
    {
        private readonly ILLMPipeline _pipeline;
        private readonly IToolCatalog _tools;
        private readonly IToolCallParser _parser;
        private readonly ITokenEstimator _estimator;
        private readonly IContextBudgetManager _trimmer;
        private readonly ITokenManager _tokenManager;
        private readonly IRetryPolicy _retryPolicy;
        private readonly ILogger<ILLMClient> _logger;
        protected LLMInitOptions _initOptions;

        public LLMClientBase(
            LLMInitOptions opts,
            IToolCatalog registry,
            ITokenEstimator estimator,
            IContextBudgetManager trimmer,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            IToolCallParser parser,
            ILogger<ILLMClient> logger)
        {
            _initOptions = opts;
            _tools = registry;
            _estimator = estimator;
            _trimmer = trimmer;
            _tokenManager = tokenManager;
            _retryPolicy = retryPolicy;
            _parser = parser;
            _logger = logger;

            _pipeline = new LLMPipeline(
                _estimator,
                _trimmer,
                _tokenManager,
                _retryPolicy,
                _logger
                );
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
            where TResponse : LLMResponseBase
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

        public async Task<LLMStructuredResponse> ExecuteAsync<T>(
            LLMStructuredRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            return await ExecuteWithHandlerAsync<LLMStructuredResponse>
                (request,
                new StructuredHandler(request, _parser, _tools),
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
