using AgentCore.LLM.Handlers;
using AgentCore.LLM.Pipeline;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Client
{
    public abstract class LLMClientBase : ILLMClient
    {
        protected readonly LLMInitOptions _initOptions;
        private readonly ILLMPipeline _pipeline;
        private readonly TextHandlerFactory _textFactory;
        private readonly StructuredHandlerFactory _structFactory;
        private readonly ILogger<ILLMClient> _logger;

        public LLMClientBase(
            LLMInitOptions opts,
            ILLMPipeline pipeline,
            TextHandlerFactory textFactory,
            StructuredHandlerFactory structFactory,
            ILogger<ILLMClient> logger)
        {
            _initOptions = opts;
            _pipeline = pipeline;
            _textFactory = textFactory;
            _structFactory = structFactory;
            _logger = logger;
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
        public async Task<LLMTextResponse> ExecuteAsync(
            LLMTextRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            var handler = _textFactory();  // DI-created
            return await ExecuteWithHandlerAsync<LLMTextResponse>(
                request, handler, onStream, ct);
        }

        public async Task<LLMStructuredResponse> ExecuteAsync(
            LLMStructuredRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            var handler = _structFactory();  // DI-created
            return await ExecuteWithHandlerAsync<LLMStructuredResponse>(
                request, handler, onStream, ct);
        }

    }
}
