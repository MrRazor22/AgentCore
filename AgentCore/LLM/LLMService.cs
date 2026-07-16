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
    private readonly ILLMService _provider;
    private readonly IReadOnlyList<Tool> _tools;
    private readonly ITokenCounter _tokenCounter;
    private readonly int _maxRetries;
    private readonly ILogger<LLMService> _logger;

    public LLMService(
        ILLMService provider,
        IReadOnlyList<Tool> tools,
        ITokenCounter tokenCounter, 
        int maxRetries = 3,
        ILogger<LLMService>? logger = null)
    {
        _provider = provider;
        _tools = tools;
        _tokenCounter = tokenCounter; 
        _maxRetries = maxRetries;
        _logger = logger ?? NullLogger<LLMService>.Instance;
    }

    public Task<LLMMetadata> GetModelInfoAsync(string? modelName = null, CancellationToken ct = default)
        => _provider.GetModelInfoAsync(modelName, ct);

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

        var activeTools = tools ?? _tools;
        var toolNames = activeTools?.Select(t => t.Name).ToList() ?? [];
        var toolNamesStr = string.Join(", ", toolNames);

        _logger.LogTrace("LLM request: Provider={Provider} Tools=[{ToolNames}]", _provider.GetType().Name, toolNamesStr);

        int? inputTokens = null;
        int? outputTokens = null;
        int? reasoningTokens = null;
        FinishReason? finishReason = null;
        int lastYieldedInput = 0;
        int lastYieldedOutput = 0;
        int? lastYieldedReasoning = null;

        var textAccumulator = new StringBuilder();
        var reasoningAccumulator = new StringBuilder();

        int maxRetries = _maxRetries;
        int attempt = 0;
        IAsyncEnumerator<LLMEvent>? enumerator = null;
        bool hasYielded = false;

        while (true)
        {
            attempt++;
            try
            {
                bool hasMore = false;
                try
                {
                    var content = _provider.StreamAsync(messages, options, activeTools, ct);
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
                        case Text text:
                            textAccumulator.Append(text.Value);
                            break;
                        case Reasoning reasoning:
                            reasoningAccumulator.Append(reasoning.Thought);
                            break;
                        case ToolCall toolCall:
                            yield return toolCall;
                            break;
                        case TokenUsage usage:
                            if (usage.InputTokens > 0) inputTokens = usage.InputTokens;
                            if (usage.OutputTokens > 0) outputTokens = usage.OutputTokens;
                            if (usage.ReasoningTokens.HasValue) reasoningTokens = usage.ReasoningTokens;
                            break;
                        case MetaDataEvent meta:
                            finishReason = meta.FinishReason;
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
            _tokenCounter.RecordActualCount(messages, activeTools, inputTokens.Value);
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
}
