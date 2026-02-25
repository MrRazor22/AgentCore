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
    Protocol.LLMResponse Response { get; }
    IAsyncEnumerable<IContentDelta> StreamAsync(LLMRequest request, CancellationToken ct = default);
}

public class LLMExecutor(
    ILLMStreamProvider _provider,
    ResponseAssembler _assembler,
    IContextManager _ctxManager,
    ILogger<LLMExecutor> _logger
) : ILLMExecutor
{
    public Protocol.LLMResponse Response { get; private set; } = new();

    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Response = new Protocol.LLMResponse();

        var trimmed = _ctxManager.Trim(request.Prompt, request.Options?.MaxOutputTokens);
        var attempt = request.Clone();
        attempt.Prompt = trimmed;

        _assembler.Reset(request.OutputType);
        _logger.LogTrace("LLM request: {Request}", attempt.ToCountablePayload());

        await foreach (var delta in _provider.StreamAsync(attempt, ct))
        {
            _assembler.OnDelta(delta);
            yield return delta;
        }

        CompleteResponse(sw);
    }

    private void CompleteResponse(Stopwatch sw)
    {
        _assembler.SetFinishReason(_provider.FinishReason);

        if (_provider.Usage != null)
            _assembler.SetUsage(_provider.Usage);

        Response = _assembler.Build();

        sw.Stop();
        _logger.LogTrace("LLM response: {Response}", Response.ToCountablePayload());
        _logger.LogDebug("LLM call Duration={Ms}ms FinishReason={FinishReason}", sw.ElapsedMilliseconds, Response.FinishReason);
    }
}
