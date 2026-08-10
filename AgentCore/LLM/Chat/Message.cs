using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;

public class Message
{
    [JsonPropertyName("role")]
    public Role Role { get; set; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents { get; set; } = Array.Empty<IContent>();

    [JsonConstructor]
    public Message(Role role, IReadOnlyList<IContent> contents)
    {
        Role = role;
        Contents = contents;
    }

    public Message(Role role, IContent content) : this(role, [content]) { }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }

public static class MessageExtensions
{
    public static T AddIfValid<T>(this T list, Role role, IContent? content) where T : ICollection<Message>
    {
        if (content is not null && (content is not Text t || !string.IsNullOrEmpty(t.Value))) list.Add(new Message(role, content));
        return list;
    }

    public static T AddIfValid<T>(this T list, Message? message) where T : ICollection<Message>
    {
        if (message?.Contents.Count > 0) list.Add(message);
        return list;
    }
}

