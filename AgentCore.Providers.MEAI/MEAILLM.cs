using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.AI;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts a Microsoft.Extensions.AI IChatClient to the AgentCore ILLM interface.
/// </summary>
public class MEAILLM : ILLM
{
    private readonly IChatClient _client;
    private readonly LLMCapabilities _capabilities;

    public MEAILLM(IChatClient client, LLMCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _capabilities = capabilities ?? new LLMCapabilities();
    }

    public LLMCapabilities GetCapabilities() => _capabilities;

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<AgentCore.Tools.Tool>? tools = null,
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
                    // Fallback to unstructured JSON format if schema parsing fails
                    chatOptions.ResponseFormat = ChatResponseFormat.Json;
                }
            }
        }

        if (tools is { Count: > 0 })
        {
            chatOptions.Tools = tools.Select(t => (AITool)new AgentCoreAIFunction(t)).ToList();
        }

        var seenToolCalls = new Dictionary<string, FunctionCallContent>();
        var yieldedToolCalls = new HashSet<string>();
        var indexToCallId = new Dictionary<int, string>();
        var indexToName = new Dictionary<int, string>();
        var yieldedInitialCall = new HashSet<int>();

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, chatOptions, ct).ConfigureAwait(false))
        {
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        yield return new Text(textContent.Text);
                    }
                    else if (content is TextReasoningContent reasoningContent && !string.IsNullOrEmpty(reasoningContent.Text))
                    {
                        yield return new Reasoning(reasoningContent.Text);
                    }
                    else if (content is FunctionCallContent fnCall && !string.IsNullOrEmpty(fnCall.CallId))
                    {
                        seenToolCalls[fnCall.CallId] = fnCall;
                        if (fnCall.Arguments != null && yieldedToolCalls.Add(fnCall.CallId))
                        {
                            var jsonNode = JsonSerializer.SerializeToNode(fnCall.Arguments);
                            yield return new ToolCall(fnCall.CallId, fnCall.Name, jsonNode ?? new JsonObject());
                        }
                    }
                    else if (content is UsageContent usageContent)
                    {
                        var usage = usageContent.Details;
                        yield return new TokenUsage(
                            (int)(usage.InputTokenCount ?? 0),
                            (int)(usage.OutputTokenCount ?? 0),
                            usage.ReasoningTokenCount.HasValue ? (int)usage.ReasoningTokenCount.Value : null
                        );
                    }
                }
            }

            List<ToolCall>? pendingToolCalls = null;
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
                            int index = toolCallUpdate.Index;

                            if (!string.IsNullOrEmpty(callId))
                            {
                                indexToCallId[index] = callId;
                            }
                            if (!string.IsNullOrEmpty(funcName))
                            {
                                indexToName[index] = funcName;
                            }

                            indexToCallId.TryGetValue(index, out var resolvedCallId);
                            indexToName.TryGetValue(index, out var resolvedName);

                            pendingToolCalls ??= new List<ToolCall>();

                            if (yieldedInitialCall.Add(index))
                            {
                                pendingToolCalls.Add(new ToolCall(
                                    resolvedCallId ?? string.Empty,
                                    resolvedName ?? string.Empty,
                                    JsonValue.Create(string.Empty),
                                    index
                                ));
                            }

                            if (!string.IsNullOrEmpty(argDelta))
                            {
                                pendingToolCalls.Add(new ToolCall(
                                    resolvedCallId ?? string.Empty,
                                    resolvedName ?? string.Empty,
                                    JsonValue.Create(argDelta),
                                    index
                                ));

                                if (!string.IsNullOrEmpty(resolvedCallId))
                                {
                                    yieldedToolCalls.Add(resolvedCallId);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore if raw update type does not support properties (non-OpenAI)
                }
            }

            if (pendingToolCalls != null)
            {
                foreach (var toolCall in pendingToolCalls)
                {
                    yield return toolCall;
                }
            }

            if (update.FinishReason is { } finishReason)
            {
                var val = finishReason.Value;
                var mappedReason = FinishReason.Cancelled;
                if (string.Equals(val, "stop", StringComparison.OrdinalIgnoreCase))
                {
                    mappedReason = FinishReason.Stop;
                }
                else if (string.Equals(val, "tool_calls", StringComparison.OrdinalIgnoreCase))
                {
                    mappedReason = FinishReason.ToolCall;
                }
                yield return new MetaDataEvent(mappedReason);
            }
        }

        // Cleanup pass for any tool calls that were never yielded (e.g. arguments remained null)
        foreach (var kvp in seenToolCalls)
        {
            if (yieldedToolCalls.Add(kvp.Key))
            {
                var fnCall = kvp.Value;
                var jsonNode = fnCall.Arguments != null 
                    ? JsonSerializer.SerializeToNode(fnCall.Arguments) 
                    : new JsonObject();
                yield return new ToolCall(fnCall.CallId, fnCall.Name, jsonNode ?? new JsonObject());
            }
        }
    }
}
