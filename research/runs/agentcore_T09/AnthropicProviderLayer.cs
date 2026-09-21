using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCoreT09;

public sealed record AnthropicChunk(string Type, string? Text = null, string? ToolName = null, string? ToolId = null, string? StopReason = null);

public sealed class AnthropicProviderLayer(
    Func<JsonObject, Task<IAsyncEnumerable<AnthropicChunk>>>? clientStreamFn = null,
    Func<JsonObject, Task<JsonObject>>? clientFn = null,
    ILLM? inner = null) : LLMLayer(inner)
{
    private readonly Func<JsonObject, Task<IAsyncEnumerable<AnthropicChunk>>>? _clientStreamFn = clientStreamFn;
    private readonly Func<JsonObject, Task<JsonObject>>? _clientFn = clientFn;

    public static string MapFinishReason(string providerStopReason) => providerStopReason switch
    {
        "end_turn" => "stop",
        "stop_sequence" => "stop",
        "tool_use" => "tool_calls",
        "max_tokens" => "length",
        _ => providerStopReason
    };

    public static JsonObject FormatRequest(IReadOnlyList<Message> messages, IReadOnlyList<ToolDefinition>? tools = null)
    {
        var anthropicMsgs = new JsonArray();
        string systemPrompt = "";

        foreach (var msg in messages)
        {
            if (msg.Role == Role.System)
            {
                var textParts = msg.Contents.OfType<Text>().Select(t => t.Value);
                systemPrompt = string.Join("\n", textParts);
                continue;
            }

            if (msg.Role == Role.User)
            {
                var text = string.Join("\n", msg.Contents.OfType<Text>().Select(t => t.Value));
                anthropicMsgs.Add(new JsonObject { ["role"] = "user", ["content"] = text });
            }
            else if (msg.Role == Role.Assistant)
            {
                var toolCalls = msg.Contents.OfType<ToolCall>().ToList();
                var text = string.Join("\n", msg.Contents.OfType<Text>().Select(t => t.Value));

                if (toolCalls.Count > 0)
                {
                    var contentBlocks = new JsonArray();
                    if (!string.IsNullOrEmpty(text))
                    {
                        contentBlocks.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                    }
                    foreach (var tc in toolCalls)
                    {
                        contentBlocks.Add(new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.Id,
                            ["name"] = tc.Name,
                            ["input"] = tc.ParseArguments().Args ?? new JsonObject()
                        });
                    }
                    anthropicMsgs.Add(new JsonObject { ["role"] = "assistant", ["content"] = contentBlocks });
                }
                else
                {
                    anthropicMsgs.Add(new JsonObject { ["role"] = "assistant", ["content"] = text });
                }
            }
            else if (msg.Role == Role.Tool)
            {
                var contentBlocks = new JsonArray();
                foreach (var tr in msg.Contents.OfType<ToolResult>())
                {
                    var resultText = string.Join("\n", tr.Contents.OfType<Text>().Select(t => t.Value));
                    contentBlocks.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = tr.ToolCallId,
                        ["content"] = resultText
                    });
                }
                anthropicMsgs.Add(new JsonObject { ["role"] = "user", ["content"] = contentBlocks });
            }
        }

        var req = new JsonObject { ["messages"] = anthropicMsgs };
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            req["system"] = systemPrompt;
        }

        if (tools != null && tools.Count > 0)
        {
            var toolDefs = new JsonArray();
            foreach (var t in tools)
            {
                toolDefs.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = new JsonObject { ["type"] = "object" }
                });
            }
            req["tools"] = toolDefs;
        }

        return req;
    }

    public static IMessageEvent? TranslateStreamChunk(AnthropicChunk chunk, string messageId) => chunk.Type switch
    {
        "text_delta" => new MessageDelta(messageId, Content: new TextDelta(0, chunk.Text ?? "")),
        "tool_use" => new MessageDelta(messageId, Content: new ToolCall(chunk.ToolId ?? "call_1", chunk.ToolName ?? "", chunk.Text ?? "{}")),
        _ => null
    };

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_clientStreamFn != null)
        {
            var req = FormatRequest(messages, tools);
            var stream = await _clientStreamFn(req).ConfigureAwait(false);
            var msgId = Guid.NewGuid().ToString("N");

            yield return new MessageStart(Role.Assistant, Id: msgId);
            await foreach (var chunk in stream.WithCancellation(ct).ConfigureAwait(false))
            {
                var evt = TranslateStreamChunk(chunk, msgId);
                if (evt != null) yield return evt;
            }
            yield return new MessageEnd(Id: msgId);
            yield break;
        }

        if (_clientFn != null)
        {
            var req = FormatRequest(messages, tools);
            var res = await _clientFn(req).ConfigureAwait(false);
            var msgId = Guid.NewGuid().ToString("N");

            yield return new MessageStart(Role.Assistant, Id: msgId);

            var stopReason = res["stop_reason"]?.ToString() ?? "end_turn";
            var finishReason = MapFinishReason(stopReason);

            if (res["content"] is JsonArray contentArr)
            {
                foreach (var node in contentArr)
                {
                    var type = node?["type"]?.ToString();
                    if (type == "text")
                    {
                        var text = node?["text"]?.ToString() ?? "";
                        yield return new MessageDelta(msgId, Content: new Text(text));
                    }
                    else if (type == "tool_use")
                    {
                        var id = node?["id"]?.ToString() ?? "call_1";
                        var name = node?["name"]?.ToString() ?? "";
                        var input = node?["input"]?.ToJsonString() ?? "{}";
                        yield return new MessageDelta(msgId, Content: new ToolCall(id, name, input));
                    }
                }
            }
            else if (res["content"] is JsonValue val)
            {
                yield return new MessageDelta(msgId, Content: new Text(val.ToString()));
            }

            yield return new MessageEnd(Id: msgId);
            yield break;
        }

        if (Inner != null)
        {
            await foreach (var evt in Inner.GenerateAsync(messages, tools, responseSchema, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }
}
