using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace AgentCore.LLM;

public interface ILLMExecutor
{
    IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        CancellationToken ct = default);
}

public class LLMExecutor(
    ILLMProvider _provider,
    IToolRegistry _toolRegistry,
    IContextManager _ctxManager,
    ILogger<LLMExecutor> _logger
) : ILLMExecutor
{
    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var trimmed = _ctxManager.Trim([.. messages], options.MaxOutputTokens);
        IReadOnlyList<Message> trimmedList = [.. trimmed];

        var tools = _toolRegistry.Tools;

        _logger.LogTrace("LLM request: {Model} {Options}", options.Model, options);

        var content = _provider.StreamAsync(trimmedList, options, tools, ct);

        string toolId = "";
        string toolName = "";
        string toolArgs = "";
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;

        await foreach (var delta in content.WithCancellation(ct))
        {
            switch (delta)
            {
                case TextDelta t:
                    yield return new TextEvent(t.Value);
                    break;

                case ToolCallDelta tc:
                    if (!string.IsNullOrEmpty(tc.Id)) toolId = tc.Id;
                    if (!string.IsNullOrEmpty(tc.Name)) toolName = tc.Name;
                    if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) toolArgs += tc.ArgumentsDelta;
                    break;

                case MetaDelta m:
                    tokenUsage = m.TokenUsage;
                    finishReason = m.FinishReason;
                    break;
            }
        }

        if (!string.IsNullOrEmpty(toolName))
        {
            JsonObject args = toolArgs.TryParseCompleteJson(out var parsed)
                ? parsed ?? new JsonObject()
                : new JsonObject();

            yield return new ToolCallEvent(new ToolCall(toolId, toolName, args));
        }

        yield return new CompletedEvent();

        sw.Stop();
        _logger.LogDebug("LLM call finished: {FinishReason} Duration={Ms}ms", finishReason, sw.ElapsedMilliseconds);
        if (tokenUsage != null)
            _logger.LogTrace("Token usage: In={In} Out={Out}", tokenUsage.InputTokens, tokenUsage.OutputTokens);
    }
}
