using AgentCore.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.Chat
{
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
        {
            if (string.IsNullOrWhiteSpace(text)) return convo;
            return convo.Add(Role.User, new TextContent(text!));
        }

        public static Conversation AddSystem(this Conversation convo, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return convo;
            return convo.Add(Role.System, new TextContent(text!));
        }

        public static Conversation AddAssistant(this Conversation convo, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return convo;
            return convo.Add(Role.Assistant, new TextContent(text!));
        }

        public static Conversation AddAssistantToolCall(this Conversation convo, ToolCall? call)
        {
            if (call == null) return convo;
            return convo.Add(Role.Assistant, call);
        }

        public static Conversation AddToolResult(this Conversation convo, ToolCallResult? result)
        {
            if (result == null) return convo;
            return convo.Add(Role.Tool, result);
        }

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
        {
            var items = chat.GetSerializableMessages(filter);
            return JsonConvert.SerializeObject(items, Formatting.Indented);
        }

        public static bool IsLastAssistantMessageSame(this Conversation chat, string newMessage)
        {
            if (string.IsNullOrWhiteSpace(newMessage))
                return false;

            var last = chat.LastOrDefault(m =>
                m.Role == Role.Assistant &&
                m.Content is TextContent &&
                !string.IsNullOrWhiteSpace(((TextContent)m.Content).Text));

            if (last == null) return false;

            var lastText = last.Content as TextContent;
            return lastText != null &&
                   string.Equals(lastText.Text.Trim(), newMessage.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        public static bool ExistsIn(this ToolCall call, Conversation chat, IEnumerable<ToolCall>? also = null)
        {
            var key = call.Arguments?.NormalizeArgs() ?? "";

            return (also?.Any(c =>
                        c.Name == call.Name &&
                        (c.Arguments?.NormalizeArgs() ?? "") == key) ?? false)
                   || chat.Any(m =>
                        m.Content is ToolCall t &&
                        t.Name == call.Name &&
                        (t.Arguments?.NormalizeArgs() ?? "") == key);
        }


        public static object? GetLastToolCallResult(this Conversation chat, ToolCall toolCall)
        {
            var argKey = toolCall.Arguments != null ? toolCall.Arguments.NormalizeArgs() : "";

            var lastResult = chat.LastOrDefault(m =>
                m.Role == Role.Tool &&
                m.Content is ToolCallResult &&
                ((ToolCallResult)m.Content).Call.Name == toolCall.Name &&
                ((ToolCallResult)m.Content).Call.Arguments.NormalizeArgs() == argKey);

            if (lastResult == null) return null;

            var result = lastResult.Content as ToolCallResult;
            if (result == null) return null;

            return result.Result;
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
                if (ShouldInclude(msg, filter))
                    yield return msg;
        }

        private static bool ShouldInclude(Chat chat, ChatFilter filter)
        {
            return chat.Role switch
            {
                Role.System => (filter & ChatFilter.System) != 0,
                Role.User => (filter & ChatFilter.User) != 0,
                Role.Tool => (filter & ChatFilter.ToolResults) != 0,
                Role.Assistant => chat.Content switch
                {
                    ToolCall _ => (filter & ChatFilter.ToolCalls) != 0,
                    TextContent _ => (filter & ChatFilter.Assistant) != 0,
                    _ => false
                },
                _ => false
            };
        }

        // This generates the standard API structure used by OpenAI/Anthropic/Mistral
        public static List<Dictionary<string, object>> GetSerializableMessages(this Conversation chat, ChatFilter filter = ChatFilter.All)
        {
            var items = new List<Dictionary<string, object>>();

            foreach (var c in chat)
            {
                if (!ShouldInclude(c, filter))
                    continue;

                var msg = new Dictionary<string, object>();

                // Standardize Role: Always lowercase (user, assistant, system, tool)
                msg["role"] = c.Role.ToString().ToLowerInvariant();

                // Case 1: Text Content
                if (c.Content is TextContent text)
                {
                    msg["content"] = text.Text;
                }
                // Case 2: Tool Call (The Request)
                else if (c.Content is ToolCall call && (filter & ChatFilter.ToolCalls) != 0)
                {
                    // detected inline tool calls have content, use "tool_calls" array
                    msg["content"] = call.Message;
                    msg["tool_calls"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "id", call.Id },
                        { "type", "function" }, // API standard field
                        { "function", new Dictionary<string, object>
                            {
                                { "name", call.Name },
                                // Parse JSON string to Object so it serializes clean, 
                                // or fallback to empty object
                                { "arguments", call.Arguments != null
                                    ? JObject.Parse(call.Arguments.ToString())
                                    : new JObject()
                                }
                            }
                        }
                    }
                };
                }
                // Case 3: Tool Result (The Response)
                else if (c.Content is ToolCallResult result && (filter & ChatFilter.ToolResults) != 0)
                {
                    // OpenAI Standard for results:
                    // Role must be "tool", Needs "tool_call_id", Content is the result string

                    msg["tool_call_id"] = result.Call.Id;
                    msg["content"] = result.Result;

                    // Note: Your old code had custom fields like 'tool_name'. 
                    // If you strictly need them for internal logs, add them conditionally, 
                    // but they aren't sent to the LLM.
                    // msg["_debug_tool_name"] = result.Call.Name; 
                }

                items.Add(msg);
            }

            return items;
        }
    }
}
