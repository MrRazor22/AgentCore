using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using AgentCore.Providers;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AgentCore.LLM.Execution;

public interface ILLMExecutor
{
    LLMResponse Response { get; }
    IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken ct = default);
}

public class LLMExecutor(
    ILLMStreamProvider _provider,
    IRetryPolicy _retryPolicy,
    StreamProcessor _processor,
    IContextManager _ctxManager,
    ILogger<LLMExecutor> _logger
) : ILLMExecutor
{
    public LLMResponse Response { get; private set; } = new();

    public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Response = new LLMResponse();

        var trimmed = _ctxManager.Trim(request.Prompt, request.Options?.MaxOutputTokens);
        var attempt = request.Clone();
        attempt.Prompt = trimmed;

        _processor.OnRequest(attempt);
        _logger.LogTrace("LLM request: {Request}", attempt.ToCountablePayload());

        await foreach (var chunk in _retryPolicy.ExecuteStreamingAsync<LLMStreamChunk>(
            attempt.Prompt,
            conversation => StreamWithProcessing(conversation, request, ct),
            ct))
        {
            yield return chunk;
        }

        CompleteResponse(sw);
    }

    private async IAsyncEnumerable<LLMStreamChunk> StreamWithProcessing(
        IList<Message> conversation,
        LLMRequest requestTemplate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var req = new LLMRequest(conversation, requestTemplate.ToolCallMode)
        {
            AvailableTools = requestTemplate.AvailableTools
        };

        await foreach (var chunk in _provider.StreamAsync(req, ct))
        {
            _processor.OnChunk(chunk);
            yield return chunk;
        }
    }

    private void CompleteResponse(Stopwatch sw)
    {
        _processor.OnResponse(Response);
        sw.Stop();
        _logger.LogTrace("LLM response: {Response}", Response.ToCountablePayload());
        _logger.LogDebug("LLM call Duration={Ms}ms FinishReason={FinishReason}", sw.ElapsedMilliseconds, Response.FinishReason);
    }
}
