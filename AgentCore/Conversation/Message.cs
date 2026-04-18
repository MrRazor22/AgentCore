using System.Text.Json.Serialization;

namespace AgentCore.Conversation;

public class Message
{
    [JsonPropertyName("role")]
    public Role Role { get; set; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents { get; set; } = Array.Empty<IContent>();

    [JsonPropertyName("kind")]
    public MessageKind Kind { get; set; } = MessageKind.Default;

    [JsonConstructor]
    public Message() { }

    public Message(Role role, IContent content, MessageKind kind = MessageKind.Default)
    {
        Role = role;
        Contents = [content];
        Kind = kind;
    }

    public Message(Role role, IReadOnlyList<IContent> contents, MessageKind kind = MessageKind.Default)
    {
        Role = role;
        Contents = contents;
        Kind = kind;
    }
}
