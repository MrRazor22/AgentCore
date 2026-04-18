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

    internal static IList<Message> GetActiveWindow(this IList<Message> messages, IReadOnlyList<Message>? system = null)
    {
        var result = new List<Message>();
        if (system != null) result.AddRange(system);

        int startIndex = 0;
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if ((messages[i].Kind & MessageKind.Summary) != 0)
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex > 0 && messages[startIndex - 1].Role == Role.User && (messages[startIndex - 1].Kind & MessageKind.Synthetic) != 0)
        {
            result.Add(messages[startIndex - 1]);
        }

        for (int i = startIndex; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i == startIndex && (msg.Kind & MessageKind.Summary) != 0)
            {
                var filteredContents = msg.Contents.Where(c => c is not Reasoning).ToList();
                result.Add(new Message(msg.Role, filteredContents, msg.Kind));
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
}
