using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Chat;

[Flags]
public enum MessageKinds
{
    None = 0,
    System = 1 << 0,
    User = 1 << 1,
    Assistant = 1 << 2,
    ToolCalls = 1 << 3,
    ToolResults = 1 << 4,
    All = System | User | Assistant | ToolCalls | ToolResults
}

public static class Extensions
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public static IList<Message> AddUser(this IList<Message> convo, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return convo;
        convo.Add(new Message(Role.User, new Text(text!)));
        return convo;
    }

    public static IList<Message> AddSystem(this IList<Message> convo, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return convo;
        convo.Add(new Message(Role.System, new Text(text!)));
        return convo;
    }

    public static IList<Message> AddAssistant(this IList<Message> convo, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return convo;
        convo.Add(new Message(Role.Assistant, new Text(text!)));
        return convo;
    }

    public static IList<Message> AddAssistantToolCall(this IList<Message> convo, ToolCall? call)
    {
        if (call == null) return convo;
        convo.Add(new Message(Role.Assistant, call));
        return convo;
    }

    public static IList<Message> AddToolResult(this IList<Message> convo, ToolResult? result)
    {
        if (result == null) return convo;
        convo.Add(new Message(Role.Tool, result));
        return convo;
    }

    public static IList<Message> Clone(this IList<Message> source, MessageKinds filter = MessageKinds.All)
    {
        var copy = new List<Message>();
        foreach (var message in source)
        {
            if (!ShouldInclude(message, filter)) continue;
            copy.Add(new Message(message.Role, message.Content));
        }
        return copy;
    }

    public static string ToJson(this IList<Message> chat, MessageKinds filter = MessageKinds.All)
        => JsonSerializer.Serialize(chat.GetSerializableMessages(filter), IndentedOptions);

    public static IList<Message> FromJson(string json)
    {
        var items = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
        if (items == null) return new List<Message>();

        var messages = new List<Message>();
        foreach (var item in items)
        {
            var roleStr = item.TryGetValue("role", out var r) ? r.GetString() ?? "user" : "user";
            var role = Enum.TryParse<Role>(roleStr, true, out var parsed) ? parsed : Role.User;

            if (item.TryGetValue("tool_call_id", out var callIdEl))
            {
                var callId = callIdEl.GetString() ?? "";
                var resultText = item.TryGetValue("content", out var c) ? c.GetString() ?? "" : "";
                messages.Add(new Message(Role.Tool, new ToolResult(callId, new Text(resultText))));
                continue;
            }

            if (item.TryGetValue("content", out var textContentEl) && textContentEl.ValueKind == JsonValueKind.String)
            {
                var text = textContentEl.GetString();
                // Avoid yielding the hacky tool name string that older AgentCore versions serialized
                if (!string.IsNullOrEmpty(text) && (!item.ContainsKey("tool_calls") || !item["tool_calls"].EnumerateArray().Any(tc => tc.TryGetProperty("function", out var f) && f.TryGetProperty("name", out var n) && n.GetString() == text)))
                {
                    messages.Add(new Message(role, new Text(text)));
                }
            }

            if (item.TryGetValue("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in toolCallsEl.EnumerateArray())
                {
                    var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var name = "";
                    var args = new JsonObject();

                    if (tc.TryGetProperty("function", out var fn))
                    {
                        name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (fn.TryGetProperty("arguments", out var a))
                        {
                            try { args = JsonNode.Parse(a.GetRawText())?.AsObject() ?? new JsonObject(); } catch { }
                        }
                    }

                    messages.Add(new Message(role, new ToolCall(id, name, args)));
                }
            }
        }

        return messages;
    }



    private static bool ShouldInclude(Message chat, MessageKinds filter) => chat.Role switch
    {
        Role.System => (filter & MessageKinds.System) != 0,
        Role.User => (filter & MessageKinds.User) != 0,
        Role.Tool => (filter & MessageKinds.ToolResults) != 0,
        Role.Assistant => chat.Content switch
        {
            ToolCall => (filter & MessageKinds.ToolCalls) != 0,
            Text => (filter & MessageKinds.Assistant) != 0,
            _ => false
        },
        _ => false
    };

    public static List<Dictionary<string, object>> GetSerializableMessages(this IList<Message> chat, MessageKinds filter = MessageKinds.All)
    {
        var items = new List<Dictionary<string, object>>();

        foreach (var c in chat)
        {
            if (!ShouldInclude(c, filter)) continue;

            var msg = new Dictionary<string, object> { ["role"] = c.Role.ToString().ToLowerInvariant() };

            if (c.Content is Text text)
            {
                msg["content"] = text.Value;
            }
            else if (c.Content is ToolCall call && (filter & MessageKinds.ToolCalls) != 0)
            {
                msg["tool_calls"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object>
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.Arguments ?? new JsonObject()
                        }
                    }
                };
            }
            else if (c.Content is ToolResult result && (filter & MessageKinds.ToolResults) != 0)
            {
                msg["tool_call_id"] = result.CallId;
                msg["content"] = result.Result?.ForLlm() ?? "";
            }

            items.Add(msg);
        }

        return items;
    }


}
