using AgentCore.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentCore.Chat;

[Flags]
public enum ChatFilter
{
    None = 0,
    System = 1 << 0,
    User = 1 << 1,
    Assistant = 1 << 2,
    ToolCalls = 1 << 3,
    ToolResults = 1 << 4,
    All = System | User | Assistant | ToolCalls | ToolResults
}

public static class ConversationExtensions
{
    public static Conversation AddUser(this Conversation convo, string? text)
        => string.IsNullOrWhiteSpace(text) ? convo : convo.Add(Role.User, new TextContent(text!));

    public static Conversation AddSystem(this Conversation convo, string? text)
        => string.IsNullOrWhiteSpace(text) ? convo : convo.Add(Role.System, new TextContent(text!));

    public static Conversation AddAssistant(this Conversation convo, string? text)
        => string.IsNullOrWhiteSpace(text) ? convo : convo.Add(Role.Assistant, new TextContent(text!));

    public static Conversation AddAssistantToolCall(this Conversation convo, ToolCall? call)
        => call == null ? convo : convo.Add(Role.Assistant, call);

    public static Conversation AddToolResult(this Conversation convo, ToolCallResult? result)
        => result == null ? convo : convo.Add(Role.Tool, result);

    public static Conversation Clone(this Conversation source, ChatFilter filter = ChatFilter.All)
    {
        var copy = new Conversation();
        foreach (var message in source)
        {
            if (!ShouldInclude(message, filter)) continue;
            copy.Add(message.Role, message.Content);
        }
        return copy;
    }

    public static string ToJson(this Conversation chat, ChatFilter filter = ChatFilter.All)
        => JsonConvert.SerializeObject(chat.GetSerializableMessages(filter), Formatting.Indented);

    public static bool IsLastAssistantMessageSame(this Conversation chat, string newMessage)
    {
        if (string.IsNullOrWhiteSpace(newMessage)) return false;

        var last = chat.LastOrDefault(m =>
            m.Role == Role.Assistant && m.Content is TextContent t && !string.IsNullOrWhiteSpace(t.Text));

        return last?.Content is TextContent lastText &&
               string.Equals(lastText.Text.Trim(), newMessage.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool ExistsIn(this ToolCall call, Conversation chat, IEnumerable<ToolCall>? also = null)
    {
        var key = call.Arguments?.NormalizeArgs() ?? "";
        return (also?.Any(c => c.Name == call.Name && (c.Arguments?.NormalizeArgs() ?? "") == key) ?? false)
            || chat.Any(m => m.Content is ToolCall t && t.Name == call.Name && (t.Arguments?.NormalizeArgs() ?? "") == key);
    }

    public static object? GetLastToolCallResult(this Conversation chat, ToolCall toolCall)
    {
        var argKey = toolCall.Arguments?.NormalizeArgs() ?? "";
        var lastResult = chat.LastOrDefault(m =>
            m.Role == Role.Tool && m.Content is ToolCallResult r &&
            r.Call.Name == toolCall.Name && r.Call.Arguments.NormalizeArgs() == argKey);

        return (lastResult?.Content as ToolCallResult)?.Result;
    }

    public static Conversation AppendToolCallResult(this Conversation chat, ToolCallResult result)
    {
        chat.AddAssistantToolCall(result.Call);
        chat.AddToolResult(result);
        return chat;
    }

    public static IEnumerable<Chat> Filter(this Conversation convo, ChatFilter filter)
    {
        foreach (var msg in convo)
            if (ShouldInclude(msg, filter)) yield return msg;
    }

    private static bool ShouldInclude(Chat chat, ChatFilter filter) => chat.Role switch
    {
        Role.System => (filter & ChatFilter.System) != 0,
        Role.User => (filter & ChatFilter.User) != 0,
        Role.Tool => (filter & ChatFilter.ToolResults) != 0,
        Role.Assistant => chat.Content switch
        {
            ToolCall => (filter & ChatFilter.ToolCalls) != 0,
            TextContent => (filter & ChatFilter.Assistant) != 0,
            _ => false
        },
        _ => false
    };

    public static List<Dictionary<string, object>> GetSerializableMessages(this Conversation chat, ChatFilter filter = ChatFilter.All)
    {
        var items = new List<Dictionary<string, object>>();

        foreach (var c in chat)
        {
            if (!ShouldInclude(c, filter)) continue;

            var msg = new Dictionary<string, object> { ["role"] = c.Role.ToString().ToLowerInvariant() };

            if (c.Content is TextContent text)
            {
                msg["content"] = text.Text;
            }
            else if (c.Content is ToolCall call && (filter & ChatFilter.ToolCalls) != 0)
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
                            ["arguments"] = call.Arguments ?? new JObject()
                        }
                    }
                };
            }
            else if (c.Content is ToolCallResult result && (filter & ChatFilter.ToolResults) != 0)
            {
                msg["tool_call_id"] = result.Call.Id;
                msg["content"] = result.Result ?? "";
            }

            items.Add(msg);
        }

        return items;
    }

    public static void RemoveToolCallBlock(this Conversation convo, Chat toolMsg)
    {
        int idx = convo.IndexOf(toolMsg);
        if (idx <= 0) return;

        var prev = convo[idx - 1];
        if (prev.Role == Role.Assistant && prev.Content is ToolCall)
            convo.Remove(prev);

        convo.Remove(toolMsg);
    }
}
