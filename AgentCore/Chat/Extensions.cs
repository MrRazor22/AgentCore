using AgentCore.Json;
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

    public static bool IsLastAssistantMessageSame(this IList<Message> chat, string newMessage)
    {
        if (string.IsNullOrWhiteSpace(newMessage)) return false;

        var last = chat.LastOrDefault(m =>
            m.Role == Role.Assistant && m.Content is Text t && !string.IsNullOrWhiteSpace(t.Value));

        return last?.Content is Text lastText &&
               string.Equals(lastText.Value.Trim(), newMessage.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool ExistsIn(this ToolCall call, IList<Message> chat, IEnumerable<ToolCall>? also = null)
    {
        var key = call.Arguments?.NormalizeArgs() ?? "";
        return (also?.Any(c => c.Name == call.Name && (c.Arguments?.NormalizeArgs() ?? "") == key) ?? false)
            || chat.Any(m => m.Content is ToolCall t && t.Name == call.Name && (t.Arguments?.NormalizeArgs() ?? "") == key);
    }

    public static object? GetLastToolCallResult(this IList<Message> chat, ToolCall toolCall)
    {
        var argKey = toolCall.Arguments?.NormalizeArgs() ?? "";
        var lastResult = chat.LastOrDefault(m =>
            m.Role == Role.Tool && m.Content is ToolResult r &&
            r.Call.Name == toolCall.Name && r.Call.Arguments.NormalizeArgs() == argKey);

        return (lastResult?.Content as ToolResult)?.Result;
    }

    public static IList<Message> AppendToolCallResult(this IList<Message> chat, ToolResult result)
    {
        chat.AddAssistantToolCall(result.Call);
        chat.AddToolResult(result);
        return chat;
    }

    public static IEnumerable<Message> Filter(this IList<Message> convo, MessageKinds filter)
    {
        foreach (var msg in convo)
            if (ShouldInclude(msg, filter)) yield return msg;
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
                msg["content"] = call.Message ?? "";
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
                msg["tool_call_id"] = result.Call.Id;
                msg["content"] = result.Result ?? "";
            }

            items.Add(msg);
        }

        return items;
    }

    public static void RemoveToolCallBlock(this IList<Message> convo, Message toolMsg)
    {
        int idx = convo.IndexOf(toolMsg);
        if (idx <= 0) return;

        var prev = convo[idx - 1];
        if (prev.Role == Role.Assistant && prev.Content is ToolCall)
            convo.Remove(prev);

        convo.Remove(toolMsg);
    }
}
