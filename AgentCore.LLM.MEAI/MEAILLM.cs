using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts a Microsoft.Extensions.AI IChatClient to the AgentCore ILLM event-streaming interface.
/// </summary>
public class MEAILLM : ILLM
{
    private readonly IChatClient _client;

    public MEAILLM(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        return StreamEventsCoreAsync(messages, responseSchema, tools, ct);
    }

    private async IAsyncEnumerable<IMessageEvent> StreamEventsCoreAsync(
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
        int? activeTextIndex = null;
        int? activeReasoningIndex = null;
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

            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
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
                            yield return new ReasoningDelta(activeReasoningIndex.Value, reasoning.Text);
                            break;

                        case TextContent text when !string.IsNullOrEmpty(text.Text):
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
                            yield return new TextDelta(activeTextIndex.Value, text.Text);
                            break;

                        case FunctionCallContent fnCall:
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

                            var callId = string.IsNullOrEmpty(fnCall.CallId) ? (fnCall.Name ?? "fn_call") : fnCall.CallId;
                            if (!toolIndices.TryGetValue(callId, out int toolIdx))
                            {
                                toolIdx = nextBlockIndex++;
                                toolIndices[callId] = toolIdx;
                                yield return new ToolCallStart(toolIdx, callId, fnCall.Name ?? "");
                            }

                            if (fnCall.Arguments != null)
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

        if (activeReasoningIndex != null)
        {
            yield return new ReasoningEnd(activeReasoningIndex.Value);
        }

        if (activeTextIndex != null)
        {
            yield return new TextEnd(activeTextIndex.Value);
        }

        foreach (var (_, toolIdx) in toolIndices)
        {
            yield return new ToolCallEnd(toolIdx);
        }

        yield return new MessageEnd(finalFinishReason, new TokenUsage(inputTokens, outputTokens));
    }
}
