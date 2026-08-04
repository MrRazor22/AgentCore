using AgentCore.LLM.Chat;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts a Microsoft.Extensions.AI IChatClient to the AgentCore ILLM interface.
/// </summary>
public class MEAILLM : ILLM
{
    private readonly IChatClient _client;

    public MEAILLM(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<AgentCore.Tools.ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatMessages = messages.Select(m => m.ToMEAIMessage()).ToList();

        var chatOptions = new ChatOptions();
        if (options != null)
        {
            if (options.Model != null)
            {
                chatOptions.ModelId = options.Model;
            }
            if (options.Temperature.HasValue)
            {
                chatOptions.Temperature = options.Temperature.Value;
            }
            if (options.MaxOutputTokens.HasValue)
            {
                chatOptions.MaxOutputTokens = options.MaxOutputTokens.Value;
            }
            if (options.ResponseSchema != null)
            {
                try
                {
                    var schemaJson = options.ResponseSchema.ToString();
                    var jsonElement = JsonSerializer.Deserialize<JsonElement>(schemaJson);
                    chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(jsonElement);
                }
                catch
                {
                    chatOptions.ResponseFormat = ChatResponseFormat.Json;
                }
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

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, chatOptions, ct).ConfigureAwait(false))
        {
            var rawDeltas = new List<ToolCallDelta>();
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
                            }
                            rawDeltas.Add(new ToolCallDelta(callId ?? "", funcName, argDelta, index));
                        }
                    }
                }
                catch { }
            }

            bool yieldedReasoning = false;
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        yield return new TextDelta(textContent.Text);
                    }
                    else if (content is TextReasoningContent reasoningContent && !string.IsNullOrEmpty(reasoningContent.Text))
                    {
                        yieldedReasoning = true;
                        yield return new ReasoningDelta(reasoningContent.Text);
                    }
                    else if (content is FunctionCallContent fnCall)
                    {
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
                        yield return new ToolCallDelta(fnCall.CallId ?? "", fnCall.Name, argsStr);
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
                var rawReasoning = TryExtractReasoning(update.RawRepresentation);
                if (!string.IsNullOrEmpty(rawReasoning))
                {
                    yield return new ReasoningDelta(rawReasoning);
                }
            }

            foreach (var d in rawDeltas)
            {
                yield return d;
            }

            if (update.FinishReason is { } finishReason)
            {
                finalFinishReason = finishReason.Value;
            }
        }

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

    public static string? TryExtractReasoning(object? rawRepresentation)
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
        catch
        {
            // Fail-safe reflection exception handling
        }

        return null;
    }
}
