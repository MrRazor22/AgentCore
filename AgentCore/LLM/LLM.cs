using System;
using AgentCore.Conversation;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Tooling;
using AgentCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using AgentCore.LLM.Exceptions;

namespace AgentCore.LLM;

public interface ILLM
{
    IAsyncEnumerable<LLMEvent> StreamAsync(IReadOnlyList<Message> messages, LLMOptions options, CancellationToken ct = default);
}

internal sealed class LLMService : ILLM
{
    private readonly ILLMProvider _provider;
    private readonly IToolRegistry _toolRegistry;
    private readonly ITokenCounter _tokenCounter;
    private readonly ITokenManager _tokenManager;
    private readonly ILogger<LLMService> _logger;

    public LLMService(
        ILLMProvider provider,
        IToolRegistry toolRegistry,
        ITokenCounter tokenCounter,
        ITokenManager tokenManager,
        ILogger<LLMService> logger)
    {
        _provider = provider;
        _toolRegistry = toolRegistry;
        _tokenCounter = tokenCounter;
        _tokenManager = tokenManager;
        _logger = logger;
    }

    public IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        CancellationToken ct = default)
        => StreamInternalAsync(messages, options, ct);

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

        var toolCalls = new Dictionary<int, (string id, string name, StringBuilder args)>();
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;
        int currentToolIndex = -1;

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
            if (evt != null) yield return evt;
        }

        var effectiveUsage = tokenUsage ?? TokenUsage.Empty;

        if (tokenUsage != null)
        {
            _tokenManager.Record(tokenUsage);
            _tokenCounter.RecordActualInput(messages, tools, tokenUsage.InputTokens);
        }
        else
        {
            _logger.LogDebug("Provider did not report token usage for FinishReason={FinishReason}", finishReason);
        }

        yield return new MetaDataEvent(
            effectiveUsage,
            finishReason ?? FinishReason.Stop,
            options.Model,
            sw.Elapsed);

        sw.Stop();
        _logger.LogDebug("LLM call finished: {FinishReason} Duration={Ms}ms", finishReason, sw.ElapsedMilliseconds);
        _logger.LogTrace("Token usage: In={In} Out={Out}", effectiveUsage.InputTokens, effectiveUsage.OutputTokens);
    }

    private static ToolCallEvent? ParseToolCall(string id, string name, string argsStr)
    {
        JsonObject parsedArgs = argsStr.TryParseCompleteJson(out var parsed)
            ? parsed ?? new JsonObject()
            : new JsonObject();

        return new ToolCallEvent(new ToolCall(id, name, parsedArgs));
    }
}
