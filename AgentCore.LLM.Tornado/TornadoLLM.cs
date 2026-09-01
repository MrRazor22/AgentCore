using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentCore.LLM.Tornado;

/// <summary>
/// Adapts LLMTornado to the AgentCore ILLM event-streaming interface via direct SSE streaming.
/// Preserves real-time token-by-token streaming for reasoning, text, and tool-call deltas.
/// </summary>
public sealed class TornadoLLM(TornadoApi api, ChatModel model) : ILLM
{
    private static readonly HttpClient HttpClient = new();

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
            Stream = true,
            StreamOptions = ChatStreamOptions.KnownOptionsIncludeUsage,
            MaxTokens = 4096,
            CancellationToken = ct
        };

        var provider = api.GetProvider(model);
        var serialized = request.Serialize(provider);
        
        string url;
        if (!string.IsNullOrWhiteSpace(api.ApiUrlFormat))
        {
            var baseUri = api.ApiUrlFormat.Split("{0}")[0].TrimEnd('/');
            url = provider.Provider == LLmProviders.Anthropic
                ? (baseUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"{baseUri}/messages" : $"{baseUri}/v1/messages")
                : (baseUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"{baseUri}/chat/completions" : $"{baseUri}/v1/chat/completions");
        }
        else
        {
            url = Uri.TryCreate(serialized.Url, UriKind.Absolute, out _) 
                ? serialized.Url 
                : provider.ApiUrl(CapabilityEndpoints.Chat, serialized.Url, model);
        }

        var body = serialized.Body is string s ? s : Newtonsoft.Json.JsonConvert.SerializeObject(serialized.Body);

        using var httpRequest = provider.OutboundMessage(url, HttpMethod.Post, body, streaming: true, request);
        httpRequest.Content = new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json"));
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var auth = api.GetProviderAuthentication(provider.Provider);
        if (auth != null && !string.IsNullOrEmpty(auth.ApiKey))
        {
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", auth.ApiKey);
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {auth.ApiKey}");
        }

        if (provider.Provider == LLmProviders.Anthropic || url.Contains("/messages", StringComparison.OrdinalIgnoreCase))
        {
            httpRequest.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }

        using var response = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        bool started = false;
        int nextId = 0, inTokens = 0, outTokens = 0;
        string? finishReason = null;
        (int Id, IMessageEvent End)? activeBlock = null;
        var toolIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith(':')) continue;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

            var data = line["data:".Length..].Trim();
            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase)) break;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(data);
            }
            catch
            {
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;

                // --- Protocol A: Anthropic Messages SSE Events ---
                if (root.TryGetProperty("type", out var typeElem))
                {
                    var eventType = typeElem.GetString();
                    switch (eventType)
                    {
                        case "message_start":
                            started = true;
                            var msg = root.TryGetProperty("message", out var msgElem) ? msgElem : default;
                            var msgId = msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;
                            var modelStr = msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("model", out var mElem) ? mElem.GetString() : model.Name;
                            if (msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("usage", out var uStart) && uStart.TryGetProperty("input_tokens", out var inTok))
                            {
                                inTokens = inTok.GetInt32();
                            }
                            yield return new MessageStart(Role.Assistant, msgId, modelStr);
                            break;

                        case "content_block_start":
                            int blockIdx = root.TryGetProperty("index", out var bIdx) && bIdx.TryGetInt32(out var bi) ? bi : nextId++;
                            if (root.TryGetProperty("content_block", out var cb))
                            {
                                var cbType = cb.TryGetProperty("type", out var cbt) ? cbt.GetString() : null;
                                if (cbType == "tool_use")
                                {
                                    var toolId = cb.TryGetProperty("id", out var tId) ? tId.GetString() : $"call_{blockIdx}";
                                    var toolName = cb.TryGetProperty("name", out var tName) ? tName.GetString() : "";
                                    toolIndices[$"block_{blockIdx}"] = blockIdx;
                                    yield return new ToolCallStart(blockIdx, toolId ?? $"call_{blockIdx}", toolName ?? "");
                                }
                                else if (cbType == "thinking")
                                {
                                    activeBlock = (blockIdx, new ReasoningEnd(blockIdx));
                                    yield return new ReasoningStart(blockIdx);
                                }
                                else if (cbType == "text")
                                {
                                    activeBlock = (blockIdx, new TextEnd(blockIdx));
                                    yield return new TextStart(blockIdx);
                                }
                            }
                            break;

                        case "content_block_delta":
                            int deltaIdx = root.TryGetProperty("index", out var dIdx) && dIdx.TryGetInt32(out var di) ? di : 0;
                            if (root.TryGetProperty("delta", out var deltaElem))
                            {
                                var deltaType = deltaElem.TryGetProperty("type", out var dt) ? dt.GetString() : null;
                                if (deltaType == "input_json_delta")
                                {
                                    var pjson = deltaElem.TryGetProperty("partial_json", out var pj) ? pj.GetString() : null;
                                    if (!string.IsNullOrEmpty(pjson))
                                    {
                                        yield return new ToolCallDelta(deltaIdx, pjson);
                                    }
                                }
                                else if (deltaType == "thinking_delta")
                                {
                                    var thought = deltaElem.TryGetProperty("thinking", out var th) ? th.GetString() : null;
                                    if (!string.IsNullOrEmpty(thought))
                                    {
                                        yield return new ReasoningDelta(deltaIdx, thought);
                                    }
                                }
                                else if (deltaType == "text_delta")
                                {
                                    var text = deltaElem.TryGetProperty("text", out var tx) ? tx.GetString() : null;
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        yield return new TextDelta(deltaIdx, text);
                                    }
                                }
                            }
                            break;

                        case "content_block_stop":
                            int stopIdx = root.TryGetProperty("index", out var sIdx) && sIdx.TryGetInt32(out var si) ? si : 0;
                            if (toolIndices.Remove($"block_{stopIdx}", out var closedIdx))
                            {
                                yield return new ToolCallEnd(closedIdx);
                            }
                            else if (activeBlock is (var aId, var endEvt) && aId == stopIdx)
                            {
                                activeBlock = null;
                                yield return endEvt;
                            }
                            break;

                        case "message_delta":
                            if (root.TryGetProperty("usage", out var mdu) && mdu.TryGetProperty("output_tokens", out var outTok))
                            {
                                outTokens = outTok.GetInt32();
                            }
                            if (root.TryGetProperty("delta", out var mdDelta) && mdDelta.TryGetProperty("stop_reason", out var sr))
                            {
                                finishReason = sr.GetString();
                            }
                            break;

                        case "message_stop":
                            yield return new MessageEnd(finishReason, new TokenUsage(inTokens, outTokens));
                            yield break;
                    }
                    continue;
                }

                // --- Protocol B: OpenAI SSE Events ---
                if (!started)
                {
                    started = true;
                    var msgId = root.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;
                    yield return new MessageStart(Role.Assistant, msgId, model.Name);
                }

                if (root.TryGetProperty("usage", out var usageElem))
                {
                    if (usageElem.TryGetProperty("prompt_tokens", out var pt)) inTokens = pt.GetInt32();
                    if (usageElem.TryGetProperty("completion_tokens", out var ctElem)) outTokens = ctElem.GetInt32();
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choices[0];
                if (choice.TryGetProperty("finish_reason", out var frElem) && frElem.ValueKind == JsonValueKind.String)
                {
                    finishReason = frElem.GetString();
                }

                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                // 1. Reasoning Tokens
                string? reasoningText = null;
                if (delta.TryGetProperty("reasoning_content", out var rcElem) && rcElem.ValueKind == JsonValueKind.String)
                {
                    reasoningText = rcElem.GetString();
                }
                else if (delta.TryGetProperty("reasoning", out var rElem) && rElem.ValueKind == JsonValueKind.String)
                {
                    reasoningText = rElem.GetString();
                }

                if (!string.IsNullOrEmpty(reasoningText))
                {
                    if (activeBlock is not (var id, ReasoningEnd))
                    {
                        if (activeBlock is (_, var end)) yield return end;
                        activeBlock = (id = nextId++, new ReasoningEnd(id));
                        yield return new ReasoningStart(id);
                    }
                    yield return new ReasoningDelta(activeBlock.Value.Id, reasoningText);
                }

                // 2. Text Content
                if (delta.TryGetProperty("content", out var contentElem) && contentElem.ValueKind == JsonValueKind.String)
                {
                    var text = contentElem.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        if (activeBlock is not (var id, TextEnd))
                        {
                            if (activeBlock is (_, var end)) yield return end;
                            activeBlock = (id = nextId++, new TextEnd(id));
                            yield return new TextStart(id);
                        }
                        yield return new TextDelta(activeBlock.Value.Id, text);
                    }
                }

                // 3. Tool Calls (Real-time argument token streaming)
                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    if (activeBlock is (_, var end)) { activeBlock = null; yield return end; }

                    for (int i = 0; i < toolCalls.GetArrayLength(); i++)
                    {
                        var tc = toolCalls[i];
                        int slot = tc.TryGetProperty("index", out var idxElem) && idxElem.TryGetInt32(out var idxVal)
                            ? idxVal
                            : (toolIndices.Count > 0 ? 0 : i);

                        var idStr = tc.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;
                        var slotKey = $"slot_{slot}";

                        if (!toolIndices.TryGetValue(slotKey, out int toolIdx) &&
                            (string.IsNullOrEmpty(idStr) || !toolIndices.TryGetValue(idStr, out toolIdx)))
                        {
                            toolIndices[slotKey] = toolIdx = nextId++;
                            if (!string.IsNullOrEmpty(idStr))
                            {
                                toolIndices[idStr] = toolIdx;
                            }

                            var callId = idStr ?? $"call_{toolIdx}";
                            var callName = tc.TryGetProperty("function", out var fnElem) && fnElem.TryGetProperty("name", out var nameElem) ? nameElem.GetString() : "";
                            yield return new ToolCallStart(toolIdx, callId, callName ?? "");
                        }

                        string? args = null;
                        if (tc.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("arguments", out var argsElem))
                            {
                                args = argsElem.ValueKind == JsonValueKind.String ? argsElem.GetString() : argsElem.GetRawText();
                            }
                        }
                        else if (tc.TryGetProperty("arguments", out var directArgs))
                        {
                            args = directArgs.ValueKind == JsonValueKind.String ? directArgs.GetString() : directArgs.GetRawText();
                        }
                        else if (tc.TryGetProperty("custom", out var custom) && custom.TryGetProperty("input", out var inputElem))
                        {
                            args = inputElem.ValueKind == JsonValueKind.String ? inputElem.GetString() : inputElem.GetRawText();
                        }

                        if (!string.IsNullOrEmpty(args))
                        {
                            yield return new ToolCallDelta(toolIdx, args);
                        }
                    }
                }
            }
        }

        if (!started) yield return new MessageStart(Role.Assistant);
        if (activeBlock is (_, var finalEnd)) yield return finalEnd;

        foreach (var toolIdx in toolIndices.Values.Distinct().Order())
        {
            yield return new ToolCallEnd(toolIdx);
        }

        yield return new MessageEnd(finishReason, new TokenUsage(inTokens, outTokens));
    }
}
