using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Handlers;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
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
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
            where TResponse : LLMResponse;
    }

    public delegate IChunkHandler HandlerResolver(LLMRequest request);

    public abstract class LLMClientBase : ILLMClient
    {
        protected readonly LLMInitOptions _initOptions;
        private readonly HandlerResolver _resolver;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IContextManager _ctxManager;
        private readonly ITokenManager _tokenManager;
        private readonly IToolCallParser _parser;
        private readonly IToolCatalog _tools;
        private readonly ILogger<LLMClientBase> _logger;

        private readonly StringBuilder _toolArgBuilder = new StringBuilder();
        private ToolCall? _firstTool;
        private string? _pendingToolId;
        private string? _pendingToolName;

        public LLMClientBase(
            LLMInitOptions opts,
            IContextManager ctxManager,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            IToolCallParser parser,
            IToolCatalog tools,
            HandlerResolver resolver,
            ILogger<LLMClientBase> logger)
        {
            _initOptions = opts;
            _retryPolicy = retryPolicy;
            _ctxManager = ctxManager;
            _tokenManager = tokenManager;
            _parser = parser;
            _tools = tools;
            _resolver = resolver;
            _logger = logger;
        }


        #region abstract methods providers must implement 
        protected abstract IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequest request,
            CancellationToken ct);
        #endregion

        public async Task<TResponse> ExecuteAsync<TResponse>(
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
            where TResponse : LLMResponse
        {
            var handler = _resolver(request);
            var result = await RunAsync(request, handler, ct, onStream);
            return (TResponse)result;
        }

        private async Task<LLMResponse> RunAsync(
            LLMRequest request,
            IChunkHandler handler,
            CancellationToken ct,
            Action<LLMStreamChunk>? onStream)
        {
            // RESET per-request tool state
            _firstTool = null;
            _pendingToolId = null;
            _pendingToolName = null;
            _toolArgBuilder.Clear();

            request.AvailableTools = _tools.RegisteredTools;
            ApplyTrim(request.Prompt, request);
            handler.OnRequest(request);
            _logger.LogInformation("► LLM Request: {Msg}", request.ToString());

            FinishReason finish = FinishReason.Stop;
            TokenUsage? usageReported = null;

            async IAsyncEnumerable<LLMStreamChunk> IterateChunk(Conversation retryPrompt)
            {
                ApplyTrim(retryPrompt, request);
                request.Prompt = retryPrompt;

                await foreach (var chunk in StreamAsync(request, ct))
                {
                    onStream?.Invoke(chunk);

                    if (chunk.Kind == StreamKind.ToolCallDelta)
                        HandleToolDelta(chunk.AsToolCallDelta());
                    else
                        handler.OnChunk(chunk);

                    yield return chunk;
                }
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                    request.Prompt,
                    rp => IterateChunk(rp),
                    ct))
                {
                    if (chunk.Kind == StreamKind.Usage)
                        usageReported = chunk.AsTokenUsage();

                    if (chunk.Kind == StreamKind.Finish)
                        finish = chunk.AsFinishReason() ?? FinishReason.Stop;
                }
            }
            catch (EarlyStopException ese)
            {
                _logger.LogWarning("► EarlyStopException: {Msg}", ese.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                finish = FinishReason.Cancelled;
            }

            var response = handler.OnResponse(finish);

            ValidateToolCall(response);

            response.TokenUsage = _tokenManager.ResolveAndRecord(
                request.ToString(),
                response.ToString(),
                usageReported
            );

            sw.Stop();
            _logger.LogInformation("► LLM Response: {Msg}", response.ToString());
            _logger.LogInformation("► Call Duration: {ms} ms", sw.ElapsedMilliseconds);

            return response;
        }

        private void ValidateToolCall(LLMResponse response)
        {
            var tool = _firstTool ?? response.ToolCall;

            if (_firstTool != null && response.ToolCall != null)
            {
                _logger.LogWarning("Inline tool ignored because streamed tool call was present.");
            }

            response.ToolCall = tool;

            if (tool != null && tool.Parameters == null)
            {
                if (!_tools.Contains(tool.Name))
                    throw new RetryException($"{tool.Name}: invalid tool");

                try
                {
                    var parsed = _parser.ValidateToolCall(tool);

                    response.ToolCall = parsed;
                }
                catch (Exception ex) when (
                    ex is ToolValidationException ||
                    ex is ToolValidationAggregateException)
                {
                    throw new RetryException(ex.ToString());
                }
            }
            if (response.FinishReason == FinishReason.ToolCall && tool == null)
            {
                throw new RetryException("FinishReason.ToolCall set but no tool call found.");
            }
        }

        private void HandleToolDelta(ToolCallDelta? td)
        {
            if (td == null || string.IsNullOrEmpty(td.Delta))
                return;

            _pendingToolName ??= td.Name;
            _pendingToolId ??= td.Id ?? Guid.NewGuid().ToString();
            _toolArgBuilder.Append(td.Delta);

            var raw = _toolArgBuilder.ToString();
            if (!raw.TryParseCompleteJson(out var json))
                return;

            if (_firstTool != null)
                throw new EarlyStopException("Second tool call detected.");

            if (!_tools.Contains(_pendingToolName!))
                throw new RetryException($"{_pendingToolName}: invalid tool");

            // RAW tool only — NO validation here
            _firstTool = new ToolCall(
                _pendingToolId!,
                _pendingToolName!,
                json!
            );

            _toolArgBuilder.Clear();
            _pendingToolId = null;
            _pendingToolName = null;
        }

        private void ApplyTrim(Conversation convo, LLMRequest request)
        {
            request.Prompt = _ctxManager.Trim(
                convo,
                request.Options?.MaxOutputTokens
            );
        }
    }
}
