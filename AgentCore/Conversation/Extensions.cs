using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Conversation;

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

internal static class Extensions
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    internal static IList<Message> AddUser(this IList<Message> convo, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return convo;
        convo.Add(new Message(Role.User, new Text(text!)));
        return convo;
    }

    internal static IList<Message> AddSystem(this IList<Message> convo, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return convo;
        convo.Add(new Message(Role.System, new Text(text!)));
        return convo;
    }

    internal static IList<Message> AddAssistant(this IList<Message> convo, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return convo;
        convo.Add(new Message(Role.Assistant, new Text(text!)));
        return convo;
    }

    internal static IList<Message> AddAssistantToolCall(this IList<Message> convo, ToolCall? call)
    {
        if (call == null) return convo;
        convo.Add(new Message(Role.Assistant, call));
        return convo;
    }

    internal static IList<Message> AddToolResult(this IList<Message> convo, ToolResult? result)
    {
        if (result == null) return convo;
        convo.Add(new Message(Role.Tool, result));
        return convo;
    }

    internal static IList<Message> GetActiveWindow(this IList<Message> messages, IReadOnlyList<Message>? system = null)
    {
        var result = new List<Message>();
        if (system != null) result.AddRange(system);

        int startIndex = 0;
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Contents.Any(c => c is Summary))
            {
                startIndex = i;
                break;
            }
        }

        for (int i = startIndex; i < messages.Count; i++)
        {
            var msg = messages[i];
            // Only strip reasoning from the initial compaction boundary message itself
            if (i == startIndex && msg.Contents.Any(c => c is Summary))
            {
                var filteredContents = msg.Contents.Where(c => c is not Reasoning).ToList();
                result.Add(new Message(msg.Role, filteredContents));
            }
            else
            {
                result.Add(msg);
            }
        }
        return result;
    }

    internal static IList<Message> Clone(this IList<Message> source, MessageKinds filter = MessageKinds.All)
    {
        var copy = new List<Message>();
        foreach (var message in source)
        {
            if (!ShouldInclude(message, filter)) continue;
            copy.Add(new Message(message.Role, message.Contents));
        }
        return copy;
    }

    internal static string ToJson(this IList<Message> chat, MessageKinds filter = MessageKinds.All)
        => JsonSerializer.Serialize(chat.GetSerializableMessages(filter), IndentedOptions);

    internal static IList<Message> FromJson(string json)
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

            if (item.TryGetValue("summary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.String)
            {
                var summaryText = summaryEl.GetString();
                if (!string.IsNullOrEmpty(summaryText))
                    messages.Add(new Message(role, new Summary(summaryText)));
            }
            
            if (item.TryGetValue("reasoning", out var reasoningEl) && reasoningEl.ValueKind == JsonValueKind.String)
            {
                var reasoningText = reasoningEl.GetString();
                if (!string.IsNullOrEmpty(reasoningText))
                    messages.Add(new Message(role, new Reasoning(reasoningText)));
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
        Role.Assistant => chat.Contents.Any(c => c is ToolCall) 
            ? (filter & MessageKinds.ToolCalls) != 0 
            : chat.Contents.Any(c => c is Text) 
                ? (filter & MessageKinds.Assistant) != 0 
                : false,
        _ => false
    };

    internal static List<Dictionary<string, object>> GetSerializableMessages(this IList<Message> chat, MessageKinds filter = MessageKinds.All)
    {
        var items = new List<Dictionary<string, object>>();

        foreach (var c in chat)
        {
            if (!ShouldInclude(c, filter)) continue;

            var msg = new Dictionary<string, object> { ["role"] = c.Role.ToString().ToLowerInvariant() };

            var textContent = c.Contents.OfType<Text>().FirstOrDefault();
            if (textContent != null)
            {
                msg["content"] = textContent.Value;
            }

            var summary = c.Contents.OfType<Summary>().FirstOrDefault();
            if (summary != null)
            {
                msg["summary"] = summary.Text;
            }
            
            var reasoning = c.Contents.OfType<Reasoning>().FirstOrDefault();
            if (reasoning != null)
            {
                msg["reasoning"] = reasoning.Thought;
            }

            var toolCalls = c.Contents.OfType<ToolCall>().ToList();
            if (toolCalls.Count > 0 && (filter & MessageKinds.ToolCalls) != 0)
            {
                msg["tool_calls"] = toolCalls.Select(call => new Dictionary<string, object>
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.Arguments ?? new JsonObject()
                    }
                }).ToList();
            }

            var toolResult = c.Contents.OfType<ToolResult>().FirstOrDefault();
            if (toolResult != null && (filter & MessageKinds.ToolResults) != 0)
            {
                msg["tool_call_id"] = toolResult.CallId;
                msg["content"] = toolResult.Result?.ForLlm() ?? "";
            }

            items.Add(msg);
        }

        return items;
    }


}
