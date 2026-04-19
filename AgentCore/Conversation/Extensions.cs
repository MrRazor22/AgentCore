using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Conversation;
 
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
            if (messages[i].Metadata.TryGetValue("summary", out var value) && value is bool isSummary && isSummary)
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex > 0 && messages[startIndex - 1].Role == Role.User && messages[startIndex - 1].Metadata.TryGetValue("synthetic", out var synValue) && synValue is bool isSynthetic && isSynthetic)
        {
            result.Add(messages[startIndex - 1]);
        }

        for (int i = startIndex; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i == startIndex && msg.Metadata.TryGetValue("summary", out var sumValue) && sumValue is bool isMsgSummary && isMsgSummary)
            {
                var filteredContents = msg.Contents.Where(c => c is not Reasoning).ToList();
                result.Add(new Message(msg.Role, filteredContents, msg.Metadata));
            }
            else
            {
                result.Add(msg);
            }
        }
        return result;
    }

}
