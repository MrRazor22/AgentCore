using AgentCore.Conversation;
using AgentCore.Schema;
using AgentCore.LLM.Exceptions;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM;

public interface ILLM
{
    IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default);
}

internal sealed class LLMService : ILLM
{
    private readonly ILLMProvider _provider;
    private readonly IToolRegistry _toolRegistry;
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<LLMService> _logger;

    public LLMService(
        ILLMProvider provider,
        IToolRegistry toolRegistry,
        ITokenCounter tokenCounter,
        ILogger<LLMService>? logger = null)
    {
        _provider = provider;
        _toolRegistry = toolRegistry;
        _tokenCounter = tokenCounter;
        _logger = logger ?? NullLogger<LLMService>.Instance;
    }

    public IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
        => StreamInternalAsync(messages, options, responseSchema, ct);

    private async IAsyncEnumerable<LLMEvent> StreamInternalAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var tools = _toolRegistry.Tools;
        var toolNames = tools?.Select(t => t.Name).ToList() ?? [];
        var toolNamesStr = string.Join(", ", toolNames);

        _logger.LogTrace("LLM request: Provider={Provider} Tools=[{ToolNames}]", _provider.GetType().Name, toolNamesStr);

        var toolCalls = new Dictionary<int, (string id, string name, StringBuilder args)>();
        int? inputTokens = null;
        int? outputTokens = null;
        int? reasoningTokens = null;
        string? modelName = null;
        FinishReason? finishReason = null;
        int currentToolIndex = -1;
        int lastYieldedInput = 0;
        int lastYieldedOutput = 0;
        int? lastYieldedReasoning = null;



        int maxRetries = options.MaxRetries;
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
                    var content = _provider.StreamAsync(messages, options, tools, responseSchema, ct);
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
                                    if (evt != null)
                                    {
                                        yield return evt;
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

                        case MetaDelta m:
                            bool tokenUpdated = false;
                            if (m.InputTokens.HasValue && m.InputTokens != inputTokens) { inputTokens = m.InputTokens; tokenUpdated = true; }
                            if (m.OutputTokens.HasValue && m.OutputTokens != outputTokens) { outputTokens = m.OutputTokens; tokenUpdated = true; }
                            if (m.ReasoningTokens.HasValue && m.ReasoningTokens != reasoningTokens) { reasoningTokens = m.ReasoningTokens; tokenUpdated = true; }
                            if (m.Model is not null) modelName = m.Model;
                            if (m.FinishReason.HasValue) finishReason = m.FinishReason;

                            if (tokenUpdated)
                            {
                                lastYieldedInput = inputTokens ?? 0;
                                lastYieldedOutput = outputTokens ?? 0;
                                lastYieldedReasoning = reasoningTokens;
                                yield return new TokenUsageEvent(
                                    lastYieldedInput,
                                    lastYieldedOutput,
                                    lastYieldedReasoning);
                            }
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
            var evt = ParseToolCall(lastEntry.id, lastEntry.name, lastEntry.args.ToString());
            if (evt != null)
            {
                yield return evt;
            }
        }

        if (inputTokens.HasValue)
        {
            _tokenCounter.RecordActualInput(messages, tools, inputTokens.Value);
        }
        else
        {
            _logger.LogDebug("Provider did not report token usage for FinishReason={FinishReason}", finishReason);
        }

        int finalInput = inputTokens ?? 0;
        int finalOutput = outputTokens ?? 0;
        if (finalInput != lastYieldedInput || finalOutput != lastYieldedOutput || reasoningTokens != lastYieldedReasoning)
        {
            yield return new TokenUsageEvent(finalInput, finalOutput, reasoningTokens);
        }

        yield return new MetaDataEvent(
            finishReason ?? FinishReason.Stop,
            modelName,
            sw.Elapsed);

        sw.Stop();
        _logger.LogDebug("LLM call finished: {FinishReason} Duration={Ms}ms", finishReason, sw.ElapsedMilliseconds);
        _logger.LogTrace("Token usage: In={In} Out={Out} Reason={Reason}", inputTokens ?? 0, outputTokens ?? 0, reasoningTokens);
    }

    private static ToolCallEvent? ParseToolCall(string id, string name, string argsStr)
    {
        JsonObject? parsedArgs = null;
        try
        {
            parsedArgs = JsonNode.Parse(argsStr)?.AsObject();
        }
        catch { /* ignore failed parse */ }

        return new ToolCallEvent(new ToolCall(id, name, parsedArgs ?? new JsonObject()));
    }
}
