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

    public IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<AgentCore.Tools.ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        return StreamEventsCoreAsync(messages, responseSchema, tools, ct);
    }

    private async IAsyncEnumerable<IMessageEvent> StreamEventsCoreAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<AgentCore.Tools.ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new MessageStart(Role.Assistant);

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
        string? finalFinishReason = null;

        int nextBlockIndex = 0;
        int? activeTextIndex = null;
        int? activeReasoningIndex = null;
        var activeToolIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rawYieldedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, chatOptions, ct).ConfigureAwait(false))
        {
            var rawToolCallEvents = new List<IMessageEvent>();
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

                            if (!string.IsNullOrEmpty(callId))
                            {
                                rawYieldedIds.Add(callId);
                                if (!activeToolIndexes.TryGetValue(callId, out int toolIdx))
                                {
                                    if (activeReasoningIndex != null)
                                    {
                                        rawToolCallEvents.Add(new ReasoningEnd(activeReasoningIndex.Value));
                                        activeReasoningIndex = null;
                                    }
                                    if (activeTextIndex != null)
                                    {
                                        rawToolCallEvents.Add(new TextEnd(activeTextIndex.Value));
                                        activeTextIndex = null;
                                    }

                                    toolIdx = nextBlockIndex++;
                                    activeToolIndexes[callId] = toolIdx;
                                    rawToolCallEvents.Add(new ToolCallStart(toolIdx, callId, funcName ?? ""));
                                }

                                if (!string.IsNullOrEmpty(argDelta))
                                {
                                    rawToolCallEvents.Add(new ToolCallDelta(toolIdx, argDelta));
                                }
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
                        if (activeTextIndex != null)
                        {
                            yield return new TextEnd(activeTextIndex.Value);
                            activeTextIndex = null;
                        }
                        if (activeReasoningIndex == null)
                        {
                            activeReasoningIndex = nextBlockIndex++;
                            yield return new ReasoningStart(activeReasoningIndex.Value);
                        }
                        yield return new ReasoningDelta(activeReasoningIndex.Value, reasoningContent.Text);
                    }
                    else if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        if (activeReasoningIndex != null)
                        {
                            yield return new ReasoningEnd(activeReasoningIndex.Value);
                            activeReasoningIndex = null;
                        }
                        if (activeTextIndex == null)
                        {
                            activeTextIndex = nextBlockIndex++;
                            yield return new TextStart(activeTextIndex.Value);
                        }
                        yield return new TextDelta(activeTextIndex.Value, textContent.Text);
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

                        if (!activeToolIndexes.TryGetValue(callId, out int toolIdx))
                        {
                            if (activeReasoningIndex != null)
                            {
                                yield return new ReasoningEnd(activeReasoningIndex.Value);
                                activeReasoningIndex = null;
                            }
                            if (activeTextIndex != null)
                            {
                                yield return new TextEnd(activeTextIndex.Value);
                                activeTextIndex = null;
                            }

                            toolIdx = nextBlockIndex++;
                            activeToolIndexes[callId] = toolIdx;
                            yield return new ToolCallStart(toolIdx, callId, fnCall.Name);
                        }

                        if (!string.IsNullOrEmpty(argsStr))
                        {
                            yield return new ToolCallDelta(toolIdx, argsStr);
                        }
                    }
                    else if (content is UsageContent usageContent)
                    {
                        var usage = usageContent.Details;
                        inputTokens += (int)(usage.InputTokenCount ?? 0);
                        outputTokens += (int)(usage.OutputTokenCount ?? 0);
                    }
                }
            }

            if (!yieldedReasoning)
            {
                var rawReasoning = TryExtractReasoning(update.RawRepresentation, _logger);
                if (!string.IsNullOrEmpty(rawReasoning))
                {
                    if (activeTextIndex != null)
                    {
                        yield return new TextEnd(activeTextIndex.Value);
                        activeTextIndex = null;
                    }
                    if (activeReasoningIndex == null)
                    {
                        activeReasoningIndex = nextBlockIndex++;
                        yield return new ReasoningStart(activeReasoningIndex.Value);
                    }
                    yield return new ReasoningDelta(activeReasoningIndex.Value, rawReasoning);
                }
            }

            if (update.FinishReason is { } finishReason)
            {
                finalFinishReason = finishReason.Value;
            }
        }

        if (activeReasoningIndex != null)
        {
            yield return new ReasoningEnd(activeReasoningIndex.Value);
            activeReasoningIndex = null;
        }

        if (activeTextIndex != null)
        {
            yield return new TextEnd(activeTextIndex.Value);
            activeTextIndex = null;
        }

        foreach (var (_, toolIdx) in activeToolIndexes)
        {
            yield return new ToolCallEnd(toolIdx);
        }
        activeToolIndexes.Clear();

        var finalUsage = new TokenUsage(
            InputTokens: inputTokens,
            OutputTokens: outputTokens
        );

        yield return new MessageEnd(finalFinishReason, finalUsage);
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
