using System.Runtime.CompilerServices;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.ChatFunctions;
using LlmTornado.Code;
using LlmTornado.Common;

namespace AgentCore.LLM.Tornado;

/// <summary>
/// Adapts LLMTornado to the AgentCore ILLM event-streaming interface.
/// Supports token-by-token tool call argument streaming, reasoning extraction, and 20+ LLM providers.
/// </summary>
public class TornadoLLM : ILLM
{
    private readonly TornadoApi _api;
    private readonly ChatModel _model;

    public TornadoLLM(TornadoApi api, ChatModel model)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(model);
        _api = api;
        _model = model;
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
        var tornadoMessages = messages.Select(m => m.ToTornadoMessage()).ToList();

        var request = new ChatRequest
        {
            Model = _model,
            Messages = tornadoMessages,
            CancellationToken = ct
        };

        if (tools is { Count: > 0 })
        {
            request.Tools = tools.Select(t => t.ToTornadoTool()).ToList();
        }

        if (responseSchema != null)
        {
            try
            {
                var jsonElem = responseSchema.ToJsonElement();
                request.ResponseFormat = ChatRequestResponseFormats.StructuredJson("response", jsonElem);
            }
            catch
            {
                request.ResponseFormat = ChatRequestResponseFormats.Json;
            }
        }

        bool messageStarted = false;
        int nextBlockIndex = 0;
        int? activeTextIndex = null;
        int? activeReasoningIndex = null;
        var toolIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int inputTokens = 0;
        int outputTokens = 0;
        string? finalFinishReason = null;

        await foreach (ChatResult res in _api.Chat.StreamChatEnumerable(request).ConfigureAwait(false))
        {
            if (!messageStarted)
            {
                messageStarted = true;
                yield return new MessageStart(
                    Role: Role.Assistant,
                    Id: res.Id,
                    Model: _model.Name
                );
            }

            if (res.Usage != null)
            {
                inputTokens = res.Usage.PromptTokens;
                outputTokens = res.Usage.CompletionTokens;
            }

            if (res.Choices is { Count: > 0 })
            {
                foreach (var choice in res.Choices)
                {
                    if (choice.FinishReason is { } fr)
                    {
                        finalFinishReason = fr.ToString();
                    }

                    var delta = choice.Delta ?? choice.Message;
                    if (delta == null) continue;

                    // 1. Reasoning stream
                    var reasoning = delta.ReasoningTokens;
                    if (!string.IsNullOrEmpty(reasoning))
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
                        yield return new ReasoningDelta(activeReasoningIndex.Value, reasoning);
                    }

                    // 2. Text stream
                    if (!string.IsNullOrEmpty(delta.Content))
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
                        yield return new TextDelta(activeTextIndex.Value, delta.Content);
                    }

                    // 3. Tool calls stream (true token delta streaming)
                    if (delta.ToolCalls is { Count: > 0 })
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

                        foreach (var tc in delta.ToolCalls)
                        {
                            var callId = string.IsNullOrEmpty(tc.Id)
                                ? (tc.Index?.ToString() ?? tc.FunctionCall?.Name ?? "fn_call")
                                : tc.Id;

                            if (!toolIndices.TryGetValue(callId, out int toolIdx))
                            {
                                toolIdx = nextBlockIndex++;
                                toolIndices[callId] = toolIdx;
                                yield return new ToolCallStart(toolIdx, callId, tc.FunctionCall?.Name ?? "");
                            }

                            var args = tc.FunctionCall?.Arguments;
                            if (!string.IsNullOrEmpty(args))
                            {
                                yield return new ToolCallDelta(toolIdx, args);
                            }
                        }
                    }
                }
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
