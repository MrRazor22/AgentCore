using AgentCore.Conversation;
using AgentCore.Execution;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Tooling;
using AgentCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM;

public interface ILLMExecutor
{
    IAsyncEnumerable<LLMEvent> StreamAsync(IReadOnlyList<Message> messages, LLMOptions options, CancellationToken ct = default);
}

public sealed class LLMExecutor : ILLMExecutor
{
    private readonly ILLMProvider _provider;
    private readonly IToolRegistry _toolRegistry;
    private readonly ITokenCounter _tokenCounter;
    private readonly ITokenManager _tokenManager;
    private readonly ILogger<LLMExecutor> _logger;
    private readonly PipelineHandler<LLMCall, IAsyncEnumerable<LLMEvent>> _pipeline;

    public LLMExecutor(
        ILLMProvider provider,
        IToolRegistry toolRegistry,
        ITokenCounter tokenCounter,
        ITokenManager tokenManager,
        ILogger<LLMExecutor> logger,
        IEnumerable<PipelineMiddleware<LLMCall, IAsyncEnumerable<LLMEvent>>>? middlewares = null)
    {
        _provider = provider;
        _toolRegistry = toolRegistry;
        _tokenCounter = tokenCounter;
        _tokenManager = tokenManager;
        _logger = logger;

        _pipeline = Pipeline<LLMCall, IAsyncEnumerable<LLMEvent>>.Build(
            middlewares ?? [],
            (req, ct) => StreamInternalAsync(req.Messages, req.Options, ct));
    }

    public IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        CancellationToken ct = default)
        => _pipeline(new LLMCall(messages, options), ct);

    private async IAsyncEnumerable<LLMEvent> StreamInternalAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var tools = _toolRegistry.Tools;
        var toolNames = tools?.Select(t => t.Name).ToList() ?? [];
        var toolNamesStr = string.Join(", ", toolNames);

        _logger.LogTrace("LLM request: Model={Model} Tools=[{ToolNames}]", options.Model, toolNamesStr);

        var content = _provider.StreamAsync(messages, options, tools, ct);

        var toolCalls = new Dictionary<int, (string id, string name, StringBuilder args)>();
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;
        int currentToolIndex = -1;

        await foreach (var delta in content.WithCancellation(ct))
        {
            switch (delta)
            {
                case TextDelta t:
                    yield return new TextEvent(t.Value);
                    break;

                case ReasoningDelta r:
                    yield return new ReasoningEvent(r.Value);
                    break;

                case ToolCallDelta tc:
                    if (tc.Index != currentToolIndex && currentToolIndex != -1)
                    {
                        if (toolCalls.TryGetValue(currentToolIndex, out var prev))
                        {
                            var evt = ParseToolCall(prev.id, prev.name, prev.args.ToString());
                            if (evt != null) yield return evt;
                            toolCalls.Remove(currentToolIndex);
                        }
                    }
                    
                    currentToolIndex = tc.Index;

                    if (!toolCalls.TryGetValue(tc.Index, out var entry))
                        entry = ("", "", new StringBuilder());
                    if (!string.IsNullOrEmpty(tc.Id)) entry.id = tc.Id;
                    if (!string.IsNullOrEmpty(tc.Name)) entry.name = tc.Name;
                    if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) entry.args.Append(tc.ArgumentsDelta);
                    toolCalls[tc.Index] = entry;
                    break;

                case MetaDelta m:
                    tokenUsage = m.TokenUsage;
                    finishReason = m.FinishReason;
                    break;
            }
        }

        if (currentToolIndex != -1 && toolCalls.TryGetValue(currentToolIndex, out var lastEntry))
        {
            var evt = ParseToolCall(lastEntry.id, lastEntry.name, lastEntry.args.ToString());
            if (evt != null) yield return evt;
        }

        var effectiveUsage = tokenUsage ?? TokenUsage.Empty;
        var toolSchemaTokens = EstimateToolSchemaTokens(tools);

        if (tokenUsage != null)
        {
            _tokenManager.Record(tokenUsage);
            if (_tokenCounter is ApproximateTokenCounter approx)
            {
                approx.Calibrate(messages, Math.Max(1, tokenUsage.InputTokens - toolSchemaTokens));
            }
        }
        else
        {
            _logger.LogDebug("Provider did not report token usage for FinishReason={FinishReason}", finishReason);
        }

        yield return new LLMMetaEvent(
            effectiveUsage,
            finishReason ?? FinishReason.Stop,
            options.Model,
            sw.Elapsed,
            toolSchemaTokens);

        sw.Stop();
        _logger.LogDebug("LLM call finished: {FinishReason} Duration={Ms}ms", finishReason, sw.ElapsedMilliseconds);
        _logger.LogTrace("Token usage: In={In} Out={Out} ToolSchemas={ToolSchemas}", effectiveUsage.InputTokens, effectiveUsage.OutputTokens, toolSchemaTokens);
    }

    private static int EstimateToolSchemaTokens(IReadOnlyList<AgentCore.Tooling.Tool>? tools)
    {
        if (tools == null || tools.Count == 0) return 0;
        
        int totalChars = 0;
        foreach (var tool in tools)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(tool);
            totalChars += json?.Length ?? 0;
        }
        return (int)(totalChars / 4.0 * 1.15);
    }

    private static ToolCallEvent? ParseToolCall(string id, string name, string argsStr)
    {
        JsonObject parsedArgs = argsStr.TryParseCompleteJson(out var parsed)
            ? parsed ?? new JsonObject()
            : new JsonObject();

        return new ToolCallEvent(new ToolCall(id, name, parsedArgs));
    }
}
