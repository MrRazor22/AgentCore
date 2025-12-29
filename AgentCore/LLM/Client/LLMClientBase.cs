using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Client
{
    public sealed class LLMInitOptions
    {
        public string? BaseUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
        public int MaxRetries { get; set; } = 3;
    }

    public interface ILLMClient
    {
        Task<TResponse> ExecuteAsync<TResponse>(
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
            where TResponse : LLMResponse, new();
    }

    public delegate IChunkHandler HandlerResolver(LLMRequest request);

    public abstract class LLMClientBase : ILLMClient
    {
        protected readonly LLMInitOptions _initOptions;
        private readonly IReadOnlyList<IChunkHandler> _handlers;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IContextManager _ctxManager;
        private readonly ILogger<LLMClientBase> _logger;

        public LLMClientBase(
            LLMInitOptions opts,
            IContextManager ctxManager,
            IRetryPolicy retryPolicy,
            IEnumerable<IChunkHandler> handlers,
            ILogger<LLMClientBase> logger)
        {
            _initOptions = opts;
            _retryPolicy = retryPolicy;
            _ctxManager = ctxManager;
            _handlers = handlers.ToList(); // materialize once
            _logger = logger;
        }

        #region abstract methods providers must implement 
        protected abstract IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequest request,
            CancellationToken ct);
        #endregion

        private static readonly Dictionary<Type, HashSet<StreamKind>> _acceptCache
            = new Dictionary<Type, HashSet<StreamKind>>();

        private static HashSet<StreamKind> GetAcceptedStreams<TResponse>()
            where TResponse : LLMResponse
        {
            var type = typeof(TResponse);

            if (_acceptCache.TryGetValue(type, out var cached))
                return cached;

            var set = new HashSet<StreamKind>();
            foreach (AcceptsStreamAttribute a in
                     type.GetCustomAttributes(typeof(AcceptsStreamAttribute), true))
                set.Add(a.Kind);

            _acceptCache[type] = set;
            return set;
        }

        public async Task<TResponse> ExecuteAsync<TResponse>(
             LLMRequest request,
             CancellationToken ct = default,
             Action<LLMStreamChunk>? onStream = null)
             where TResponse : LLMResponse, new()
        {
            return await RunAsync<TResponse>(request, ct, onStream);
        }

        private async Task<TResponse> RunAsync<TResponse>(
            LLMRequest request,
            CancellationToken ct,
            Action<LLMStreamChunk>? onStream)
            where TResponse : LLMResponse, new()
        {
            var response = new TResponse();

            var accepted = GetAcceptedStreams<TResponse>();

            var handlers = _handlers
                .Where(h => accepted.Contains(h.Kind))
                .ToList();

            foreach (var h in handlers)
                h.OnRequest(request);

            var initialContext = _ctxManager.Trim(request.Prompt, request.Options?.MaxOutputTokens);

            async IAsyncEnumerable<LLMStreamChunk> Iterate(Conversation retryPrompt)
            {
                var readyPrompt = _ctxManager.Trim(retryPrompt, request.Options?.MaxOutputTokens);
                var attemptRequest = request.Clone();
                attemptRequest.Prompt = readyPrompt;

                await foreach (var chunk in StreamAsync(attemptRequest, ct))
                {
                    onStream?.Invoke(chunk);

                    if (!accepted.Contains(chunk.Kind))
                        continue;

                    foreach (var h in handlers)
                        if (h.Kind == chunk.Kind)
                            h.OnChunk(chunk);

                    yield return chunk;
                }
            }

            try
            {
                await foreach (var _ in _retryPolicy.ExecuteStreamAsync(
                    initialContext,
                    rp => Iterate(rp),
                    ct))
                { }
            }
            catch (EarlyStopException e)
            {
                _logger.LogWarning("► Early stop: {Msg}", e.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                response.FinishReason = FinishReason.Cancelled;
            }

            foreach (var h in handlers)
                h.OnResponse(response);


            return response;
        }
    }
}
