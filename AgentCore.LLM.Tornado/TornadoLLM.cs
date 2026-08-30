using System.Runtime.CompilerServices;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentCore.LLM.Tornado;

/// <summary>
/// Adapts LLMTornado to the AgentCore ILLM event-streaming interface.
/// Preserves parallel/interleaved tool calls and clean sequential text/reasoning transitions.
/// </summary>
public sealed class TornadoLLM(TornadoApi api, ChatModel model) : ILLM
{
    public async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = model,
            Messages = messages.Select(m => m.ToTornadoMessage()).ToList(),
            Tools = tools?.Select(t => t.ToTornadoTool()).ToList(),
            ResponseFormat = responseSchema != null ? ChatRequestResponseFormats.StructuredJson("response", responseSchema.ToJsonElement()) : null,
            CancellationToken = ct
        };

        bool started = false;
        int nextId = 0, inTokens = 0, outTokens = 0;
        string? finishReason = null;
        (int Id, IMessageEvent End)? activeBlock = null;
        var toolIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await foreach (var res in api.Chat.StreamChatEnumerable(request).ConfigureAwait(false))
        {
            if (!started)
            {
                started = true;
                yield return new MessageStart(Role.Assistant, res.Id, model.Name);
            }

            if (res.Usage != null) { inTokens = res.Usage.PromptTokens; outTokens = res.Usage.CompletionTokens; }
            if (res.Choices is not { Count: > 0 }) continue;

            foreach (var choice in res.Choices)
            {
                if (choice.FinishReason is { } fr) finishReason = fr.ToString();

                // Skip Tornado's synthetic end-of-stream full-message snapshot
                if (res.Id == null && res.Usage != null && choice.FinishReason == null && choice.Delta?.Content != null)
                {
                    continue;
                }

                var delta = choice.Delta ?? choice.Message;
                if (delta == null) continue;

                // 1. Reasoning Tokens
                if (delta.ReasoningTokens is { Length: > 0 } r)
                {
                    if (activeBlock is not (var id, ReasoningEnd))
                    {
                        if (activeBlock is (_, var end)) yield return end;
                        activeBlock = (id = nextId++, new ReasoningEnd(id));
                        yield return new ReasoningStart(id);
                    }
                    yield return new ReasoningDelta(activeBlock.Value.Id, r);
                }

                // 2. Text Content
                if (delta.Content is { Length: > 0 } text)
                {
                    if (activeBlock is not (var id, TextEnd))
                    {
                        if (activeBlock is (_, var end)) yield return end;
                        activeBlock = (id = nextId++, new TextEnd(id));
                        yield return new TextStart(id);
                    }
                    yield return new TextDelta(activeBlock.Value.Id, text);
                }

                // 3. Tool Calls (preserves parallel and interleaved tool streams)
                if (delta.ToolCalls is { Count: > 0 } tcs)
                {
                    if (activeBlock is (_, var end)) { activeBlock = null; yield return end; }

                    foreach (var tc in tcs)
                    {
                        var key = tc.Index.HasValue ? $"idx_{tc.Index.Value}" : (tc.Id ?? tc.FunctionCall?.Name ?? "fn_call");
                        var callId = tc.Id ?? key;

                        if (!toolIndices.TryGetValue(key, out int toolIdx))
                        {
                            toolIndices[key] = toolIdx = nextId++;
                            yield return new ToolCallStart(toolIdx, callId, tc.FunctionCall?.Name ?? "");
                        }

                        if (tc.FunctionCall?.Arguments is { Length: > 0 } args)
                        {
                            yield return new ToolCallDelta(toolIdx, args);
                        }
                    }
                }
            }
        }

        if (!started) yield return new MessageStart(Role.Assistant);
        if (activeBlock is (_, var finalEnd)) yield return finalEnd;

        foreach (var toolIdx in toolIndices.Values.Order())
        {
            yield return new ToolCallEnd(toolIdx);
        }

        yield return new MessageEnd(finishReason, new TokenUsage(inTokens, outTokens));
    }
}
