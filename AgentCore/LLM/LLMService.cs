using AgentCore.LLM.Exceptions;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using AgentCore.LLM.Schema;
using AgentCore.LLM.Chat;

namespace AgentCore.LLM;

internal sealed class LLMService : ILLMService
{
    private readonly ILLM _provider;
    private readonly ITokenCounter _tokenCounter;
    private readonly int _maxRetries;
    private readonly ILogger<LLMService> _logger;

    internal ILLM Provider => _provider;

    public LLMService(
        ILLM provider,
        ITokenCounter tokenCounter, 
        int maxRetries = 3,
        ILogger<LLMService>? logger = null)
    {
        _provider = provider;
        _tokenCounter = tokenCounter; 
        _maxRetries = maxRetries;
        _logger = logger ?? NullLogger<LLMService>.Instance;
    }

    public IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default)
        => StreamInternalAsync(messages, options, tools, ct);

    private async IAsyncEnumerable<LLMEvent> StreamInternalAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var toolNames = tools?.Select(t => t.Name).ToList() ?? [];
        var toolNamesStr = string.Join(", ", toolNames);

        _logger.LogTrace("LLM request: Provider={Provider} Tools=[{ToolNames}]", _provider.GetType().Name, toolNamesStr);

        var toolCalls = new Dictionary<int, (string id, string name, StringBuilder args)>();
        int? inputTokens = null;
        int? outputTokens = null;
        int? reasoningTokens = null;
        FinishReason? finishReason = null;
        int currentToolIndex = -1;
        int lastYieldedInput = 0;
        int lastYieldedOutput = 0;
        int? lastYieldedReasoning = null;

        var textAccumulator = new StringBuilder();
        var reasoningAccumulator = new StringBuilder();

        int maxRetries = _maxRetries;
        int attempt = 0;
        IAsyncEnumerator<IContentDelta>? enumerator = null;
        bool hasYielded = false;

        while (true)
        {
            attempt++;
            try
            {
                bool hasMore = false;
                try
                {
                    var content = _provider.StreamAsync(messages, options, tools, ct);
                    enumerator = content.GetAsyncEnumerator(ct);
                    hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (RetryableException ex) when (attempt <= maxRetries && !hasYielded)
                {
                    if (enumerator != null)
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                        enumerator = null;
                    }

                    int delayMs = (int)(Math.Pow(2, attempt) * 500) + new Random().Next(0, 200);
                    _logger.LogWarning(ex, "Transient error starting LLM stream. Retrying in {DelayMs}ms (Attempt {Attempt}/{MaxRetries}).", delayMs, attempt, maxRetries);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    continue;
                }

                while (hasMore)
                {
                    var delta = enumerator.Current;
                    hasYielded = true;

                    switch (delta)
                    {
                        case TextDelta text:
                            textAccumulator.Append(text.Value);
                            break;

                        case ReasoningDelta reasoning:
                            reasoningAccumulator.Append(reasoning.Value);
                            break;

                        case ToolCallDelta tc:
                            if (tc.Index != currentToolIndex && currentToolIndex != -1)
                            {
                                if (toolCalls.TryGetValue(currentToolIndex, out var prev))
                                {
                                    var call = BuildToolCall(prev.id, prev.name, prev.args.ToString());
                                    if (call != null)
                                    {
                                        yield return call;
                                    }
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

                        case MetaDelta meta:
                            if (meta.InputTokens.HasValue && meta.InputTokens > 0) inputTokens = meta.InputTokens;
                            if (meta.OutputTokens.HasValue && meta.OutputTokens > 0) outputTokens = meta.OutputTokens;
                            if (meta.ReasoningTokens.HasValue) reasoningTokens = meta.ReasoningTokens;
                            if (meta.FinishReason.HasValue) finishReason = meta.FinishReason;
                            break;
                    }

                    hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }

                break;
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                    enumerator = null;
                }
            }
        }

        if (currentToolIndex != -1 && toolCalls.TryGetValue(currentToolIndex, out var lastEntry))
        {
            var call = BuildToolCall(lastEntry.id, lastEntry.name, lastEntry.args.ToString());
            if (call != null)
            {
                yield return call;
            }
        }

        if (reasoningAccumulator.Length > 0)
        {
            yield return new Reasoning(reasoningAccumulator.ToString());
        }

        if (textAccumulator.Length > 0)
        {
            yield return new Text(textAccumulator.ToString());
        }

        if (inputTokens.HasValue)
        {
            _tokenCounter.RecordActualCount(messages, tools, inputTokens.Value);
        }
        else
        {
            _logger.LogDebug("Provider did not report token usage for FinishReason={FinishReason}", finishReason);
        }

        int finalInput = inputTokens ?? 0;
        int finalOutput = outputTokens ?? 0;
        if (finalInput != lastYieldedInput || finalOutput != lastYieldedOutput || reasoningTokens != lastYieldedReasoning)
        {
            yield return new TokenUsage(finalInput, finalOutput, reasoningTokens);
        }

        yield return new MetaDataEvent(
            finishReason ?? FinishReason.Stop,
            sw.Elapsed);

        sw.Stop();
        _logger.LogDebug("LLM call finished: {FinishReason} Duration={Ms}ms", finishReason, sw.ElapsedMilliseconds);
        _logger.LogTrace("Token usage: In={In} Out={Out} Reason={Reason}", inputTokens ?? 0, outputTokens ?? 0, reasoningTokens);
    }

    private static ToolCall? BuildToolCall(string id, string name, string argsStr)
    {
        JsonObject? parsedArgs = null;
        try
        {
            parsedArgs = JsonNode.Parse(argsStr)?.AsObject();
        }
        catch { /* ignore failed parse */ }

        return new ToolCall(id, name, parsedArgs ?? new JsonObject());
    }
}
