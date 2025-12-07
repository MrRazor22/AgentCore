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
    public interface ILLMClient
    {
        Task<TResponse> ExecuteAsync<TResponse>(
            LLMRequestBase request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
            where TResponse : LLMResponseBase;
    }
    public abstract class LLMClientBase : ILLMClient
    {
        protected readonly LLMInitOptions _initOptions;
        private readonly ILLMPipeline _pipeline;
        private readonly TextHandlerFactory _textFactory;
        private readonly StructuredHandlerFactory _structFactory;

        public LLMClientBase(
            LLMInitOptions opts,
            ILLMPipeline pipeline,
            TextHandlerFactory textFactory,
            StructuredHandlerFactory structFactory)
        {
            _initOptions = opts;
            _pipeline = pipeline;
            _textFactory = textFactory;
            _structFactory = structFactory;
        }


        #region abstract methods providers must implement 
        protected abstract IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequestBase request,
            CancellationToken ct);
        #endregion

        public async Task<TResponse> ExecuteAsync<TResponse>(
        LLMRequestBase request,
        CancellationToken ct = default,
        Action<LLMStreamChunk>? onStream = null)
        where TResponse : LLMResponseBase
        {
            IChunkHandler handler;

            if (request is LLMTextRequest)
            {
                handler = _textFactory();
            }
            else if (request is LLMStructuredRequest)
            {
                handler = _structFactory();
            }
            else
            {
                throw new NotSupportedException(
                    "Unsupported request type: " + request.GetType().Name);
            }

            handler.PrepareRequest(request);

            var result = await _pipeline.RunAsync(
                request,
                handler,
                r => StreamAsync(r, ct),
                onStream,
                ct);

            return (TResponse)result;
        }
    }
}
