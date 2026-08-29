using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts a Microsoft.Extensions.AI IChatClient to the AgentCore ILLM interface.
/// </summary>
public class MEAILLM : ILLM
{
    private readonly IChatClient _client;
    private readonly ILogger<MEAILLM>? _logger;

    public MEAILLM(IChatClient client, ILogger<MEAILLM>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _logger = logger;
    }

    public async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<AgentCore.Tools.ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatMessages = messages.Select(m => m.ToMEAIMessage()).ToList();

        var chatOptions = new ChatOptions();
        if (responseSchema != null)
        {
            try
            { 
                var jsonElement = responseSchema.ToJsonElement();
                chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(jsonElement);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to configure ChatResponseFormat.ForJsonSchema. Falling back to standard Json response format.");
                chatOptions.ResponseFormat = ChatResponseFormat.Json;
            }
        }

        if (tools is { Count: > 0 })
        {
            chatOptions.Tools = tools.Select(t => (AITool)new AgentCoreAIFunction(t)).ToList();
        }

        int inputTokens = 0;
        int outputTokens = 0;
        int? reasoningTokens = null;
        string? finalFinishReason = null;

        var rawYieldedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeToolCallStreams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasActiveText = false;
        bool hasActiveReasoning = false;

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, chatOptions, ct).ConfigureAwait(false))
        {
            int? choiceIndex = null;
            if (update.RawRepresentation != null)
            {
                try
                {
                    dynamic raw = update.RawRepresentation;
                    choiceIndex = (int?)raw.Index ?? (int?)raw.ChoiceIndex;
                }
                catch { }
            }

            var rawToolCallEvents = new List<ILLMOutput>();
            if (update.RawRepresentation is not null)
            {
                try
                {
                    dynamic rawUpdate = update.RawRepresentation;
                    var toolCallUpdates = rawUpdate.ToolCallUpdates;
                    if (toolCallUpdates != null)
                    {
                        foreach (dynamic toolCallUpdate in toolCallUpdates)
                        {
                            string? callId = toolCallUpdate.ToolCallId;
                            string? funcName = toolCallUpdate.FunctionName;
                            string? argDelta = toolCallUpdate.FunctionArgumentsUpdate?.ToString();
                            int? index = null;
                            try { index = (int?)toolCallUpdate.Index; } catch { }

                            if (!string.IsNullOrEmpty(callId))
                            {
                                rawYieldedIds.Add(callId);
                                if (activeToolCallStreams.Add(callId))
                                {
                                    if (hasActiveReasoning) { rawToolCallEvents.Add(new ReasoningEnd()); hasActiveReasoning = false; }
                                    if (hasActiveText) { rawToolCallEvents.Add(new TextEnd()); hasActiveText = false; }
                                    rawToolCallEvents.Add(new ToolCallStart(callId, funcName ?? "", index ?? choiceIndex));
                                }
                            }

                            if (!string.IsNullOrEmpty(argDelta) && !string.IsNullOrEmpty(callId))
                            {
                                rawToolCallEvents.Add(new ToolCallDelta(callId, argDelta, index ?? choiceIndex));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Unexpected error parsing raw tool call updates from provider response.");
                }
            }

            foreach (var evt in rawToolCallEvents)
            {
                yield return evt;
            }

            bool yieldedReasoning = false;

            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextReasoningContent reasoningContent && !string.IsNullOrEmpty(reasoningContent.Text))
                    {
                        yieldedReasoning = true;
                        hasActiveReasoning = true;
                        yield return new ReasoningDelta(reasoningContent.Text);
                    }
                    else if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        if (hasActiveReasoning)
                        {
                            yield return new ReasoningEnd();
                            hasActiveReasoning = false;
                        }
                        hasActiveText = true;
                        yield return new TextDelta(textContent.Text);
                    }
                    else if (content is FunctionCallContent fnCall)
                    {
                        var callId = fnCall.CallId ?? Guid.NewGuid().ToString("N");
                        if (!string.IsNullOrEmpty(fnCall.CallId) && rawYieldedIds.Contains(fnCall.CallId))
                        {
                            continue;
                        }

                        string argsStr = "";
                        if (fnCall.Arguments != null)
                        {
                            try
                            {
                                argsStr = JsonSerializer.Serialize(fnCall.Arguments);
                            }
                            catch { }
                        }

                        if (activeToolCallStreams.Add(callId))
                        {
                            if (hasActiveReasoning) { yield return new ReasoningEnd(); hasActiveReasoning = false; }
                            if (hasActiveText) { yield return new TextEnd(); hasActiveText = false; }
                            yield return new ToolCallStart(callId, fnCall.Name, choiceIndex);
                        }
                        if (!string.IsNullOrEmpty(argsStr))
                        {
                            yield return new ToolCallDelta(callId, argsStr, choiceIndex);
                        }
                    }
                    else if (content is UsageContent usageContent)
                    {
                        var usage = usageContent.Details;
                        inputTokens += (int)(usage.InputTokenCount ?? 0);
                        outputTokens += (int)(usage.OutputTokenCount ?? 0);
                        if (usage.ReasoningTokenCount.HasValue)
                        {
                            reasoningTokens = (reasoningTokens ?? 0) + (int)usage.ReasoningTokenCount.Value;
                        }
                    }
                }
            }

            if (!yieldedReasoning)
            {
                var rawReasoning = TryExtractReasoning(update.RawRepresentation, _logger);
                if (!string.IsNullOrEmpty(rawReasoning))
                {
                    hasActiveReasoning = true;
                    yield return new ReasoningDelta(rawReasoning);
                }
            }

            if (update.FinishReason is { } finishReason)
            {
                finalFinishReason = finishReason.Value;
            }
        }

        if (hasActiveReasoning)
        {
            yield return new ReasoningEnd();
        }

        if (hasActiveText)
        {
            yield return new TextEnd();
        }

        foreach (var callId in activeToolCallStreams)
        {
            yield return new ToolCallEnd(callId);
        }
        activeToolCallStreams.Clear();

        yield return new TokenUsage(
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            ReasoningTokens: reasoningTokens
        );

        if (finalFinishReason != null)
        {
            yield return new FinishReason(finalFinishReason);
        }
    }

    public static string? TryExtractReasoning(object? rawRepresentation, ILogger<MEAILLM>? logger = null)
    {
        if (rawRepresentation is null) return null;

        try
        {
            var rawType = rawRepresentation.GetType();
            var reasoningProp = rawType.GetProperty("ReasoningContentUpdate")
                             ?? rawType.GetProperty("ReasoningContent");

            if (reasoningProp != null)
            {
                var val = reasoningProp.GetValue(rawRepresentation);
                if (val != null)
                {
                    if (val is System.Collections.IEnumerable enumerable && val is not string)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var part in enumerable)
                        {
                            if (part != null)
                            {
                                var textProp = part.GetType().GetProperty("Text");
                                if (textProp != null)
                                {
                                    sb.Append(textProp.GetValue(part)?.ToString());
                                }
                                else
                                {
                                    sb.Append(part.ToString());
                                }
                            }
                        }
                        var result = sb.ToString();
                        return string.IsNullOrEmpty(result) ? null : result;
                    }

                    var strVal = val.ToString();
                    return string.IsNullOrEmpty(strVal) ? null : strVal;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Unexpected error extracting reasoning content from raw provider response.");
        }

        return null;
    }
}
