using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Microsoft.Extensions.AI;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts a Microsoft.Extensions.AI IChatClient to the AgentCore ILLM event-streaming interface.
/// </summary>
public sealed class MEAILLM(IChatClient client) : ILLM
{
    private readonly IChatClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
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
            catch
            {
                chatOptions.ResponseFormat = ChatResponseFormat.Json;
            }
        }

        if (tools is { Count: > 0 })
        {
            chatOptions.Tools = tools.Select(t => (AITool)new AgentCoreAIFunction(t)).ToList();
        }

        bool messageStarted = false;
        int nextBlockIndex = 0;
        (int Id, IBlockEndEvent End)? activeBlock = null;
        var toolIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int inputTokens = 0;
        int outputTokens = 0;
        string? finalFinishReason = null;

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, chatOptions, ct).ConfigureAwait(false))
        {
            if (!messageStarted)
            {
                messageStarted = true;
                yield return new MessageStart(
                    Role: Role.Assistant,
                    Id: update.ResponseId,
                    Model: update.ModelId
                );
            }

            // 1. Raw tool call updates extraction (live argument streaming)
            List<(int ToolIdx, string? CallId, string? FuncName, string? ArgDelta, bool IsNew)> rawEvents = [];
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
                            int slot = 0;
                            try { slot = (int)toolCallUpdate.Index; } catch { }

                            var slotKey = $"slot_{slot}";
                            bool isNew = false;
                            if (!toolIndices.TryGetValue(slotKey, out int toolIdx) &&
                                (string.IsNullOrEmpty(callId) || !toolIndices.TryGetValue(callId, out toolIdx)))
                            {
                                toolIdx = nextBlockIndex++;
                                toolIndices[slotKey] = toolIdx;
                                if (!string.IsNullOrEmpty(callId))
                                {
                                    toolIndices[callId] = toolIdx;
                                }
                                isNew = true;
                            }

                            rawEvents.Add((toolIdx, callId, funcName, argDelta, isNew));
                        }
                    }
                }
                catch { }
            }

            foreach (var (toolIdx, callId, funcName, argDelta, isNew) in rawEvents)
            {
                if (isNew)
                {
                    if (activeBlock is (_, var end)) { activeBlock = null; yield return end; }
                    yield return new ToolCallStart(toolIdx, callId ?? $"call_{toolIdx}", funcName ?? "");
                }

                if (!string.IsNullOrEmpty(argDelta))
                {
                    yield return new ToolCallDelta(toolIdx, argDelta);
                }
            }

            // 2. High-level contents (Text, FunctionCall fallback, Usage)
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            if (activeBlock is not (var tId, TextEnd))
                            {
                                if (activeBlock is (_, var end)) yield return end;
                                activeBlock = (tId = nextBlockIndex++, new TextEnd(tId));
                                yield return new TextStart(tId);
                            }
                            yield return new TextDelta(activeBlock.Value.Id, text.Text);
                            break;

                        case FunctionCallContent fnCall:
                            var callId = string.IsNullOrEmpty(fnCall.CallId) ? (fnCall.Name ?? "fn_call") : fnCall.CallId;
                            if (!toolIndices.TryGetValue(callId, out int toolIdx))
                            {
                                if (activeBlock is (_, var end)) { activeBlock = null; yield return end; }
                                toolIdx = nextBlockIndex++;
                                toolIndices[callId] = toolIdx;
                                yield return new ToolCallStart(toolIdx, callId, fnCall.Name ?? "");
                            }

                            if (fnCall.Arguments != null && rawEvents.Count == 0)
                            {
                                string argsStr = "";
                                try
                                {
                                    argsStr = JsonSerializer.Serialize(fnCall.Arguments);
                                }
                                catch { }

                                if (!string.IsNullOrEmpty(argsStr))
                                {
                                    yield return new ToolCallDelta(toolIdx, argsStr);
                                }
                            }
                            break;

                        case UsageContent usage:
                            var details = usage.Details;
                            inputTokens += (int)(details.InputTokenCount ?? 0);
                            outputTokens += (int)(details.OutputTokenCount ?? 0);
                            break;
                    }
                }
            }

            if (update.FinishReason is { } finishReason)
            {
                finalFinishReason = finishReason.Value;
            }
        }

        if (!messageStarted)
        {
            yield return new MessageStart(Role.Assistant);
        }

        if (activeBlock is (_, var finalEnd))
        {
            yield return finalEnd;
        }

        foreach (var toolIdx in toolIndices.Values.Distinct().Order())
        {
            yield return new ToolCallEnd(toolIdx);
        }

        yield return new MessageEnd(finalFinishReason, new TokenUsage(inputTokens, outputTokens));
    }
}
