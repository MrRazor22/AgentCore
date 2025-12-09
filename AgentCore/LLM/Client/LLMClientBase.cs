using AgentCore.LLM.Handlers;
using AgentCore.LLM.Pipeline;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Client
{
    public sealed class LLMInitOptions
    {
        public string? BaseUrl { get; set; } = null;
        public string? ApiKey { get; set; } = null;
        public string? Model { get; set; } = null;
    }

    public interface ILLMClient
    {
        Task<TResponse> ExecuteAsync<TResponse>(
            LLMRequestBase request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
            where TResponse : LLMResponseBase;
    }

    public delegate IChunkHandler HandlerResolver(LLMRequestBase request);

    public abstract class LLMClientBase : ILLMClient
    {
        protected readonly LLMInitOptions _initOptions;
        private readonly ILLMPipeline _pipeline;
        private readonly HandlerResolver _resolver;

        public LLMClientBase(
            LLMInitOptions opts,
            ILLMPipeline pipeline,
            HandlerResolver resolver)
        {
            _initOptions = opts;
            _pipeline = pipeline;
            _resolver = resolver;
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
            var handler = _resolver(request);

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
