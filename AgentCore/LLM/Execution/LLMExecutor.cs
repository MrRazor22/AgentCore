using AgentCore.Chat;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Providers;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AgentCore.LLM.Execution
{
    public interface ILLMExecutor
    {
        LLMResponse Response { get; }

        IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequest request,
            CancellationToken ct = default);
    }

    public class LLMExecutor : ILLMExecutor
    {
        private readonly ILLMStreamProvider _provider;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IReadOnlyDictionary<StreamKind, IChunkHandler> _handlers;
        private readonly IContextManager _ctxManager;
        private readonly ILogger<LLMExecutor> _logger;

        public LLMResponse Response { get; private set; } = new LLMResponse();

        public LLMExecutor(
            ILLMStreamProvider provider,
            IRetryPolicy retryPolicy,
            IEnumerable<IChunkHandler> handlers,
            IContextManager ctxManager,
            ILogger<LLMExecutor> logger)
        {
            _provider = provider;
            _retryPolicy = retryPolicy;
            _handlers = handlers.ToDictionary(h => h.Kind);
            _ctxManager = ctxManager;
            _logger = logger;
        }

        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            Response = new LLMResponse();

            var trimmed = _ctxManager.Trim(request.Prompt, request.Options?.MaxOutputTokens);

            var attempt = request.Clone();
            attempt.Prompt = trimmed;

            foreach (var h in _handlers.Values)
                h.OnRequest(attempt);

            _logger.LogTrace("LLM request: {Request}", attempt.ToCountablePayload());

            await foreach (var chunk in _retryPolicy.ExecuteStreamingAsync<LLMStreamChunk>(
                attempt.Prompt,
                conversation => StreamWithHandlers(conversation, request, ct),
                ct))
            {
                yield return chunk;
            }

            CompleteResponse(sw);
        }

        private async IAsyncEnumerable<LLMStreamChunk> StreamWithHandlers(
            Conversation conversation,
            LLMRequest requestTemplate,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var req = new LLMRequest(conversation, requestTemplate.ToolCallMode)
            {
                AvailableTools = requestTemplate.AvailableTools
            };

            await foreach (var chunk in _provider.StreamAsync(req, ct))
            {
                if (_handlers.TryGetValue(chunk.Kind, out var handler))
                    handler.OnChunk(chunk);
                yield return chunk;
            }
        }

        private void CompleteResponse(Stopwatch sw)
        {
            foreach (var h in _handlers.Values)
                h.OnResponse(Response);

            sw.Stop();
            _logger.LogTrace("LLM response: {Response}", Response.ToCountablePayload());
            _logger.LogDebug("LLM call Duration={Ms}ms FinishReason={FinishReason}",
                sw.ElapsedMilliseconds, Response.FinishReason);
        }
    }
}
