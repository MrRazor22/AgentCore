using System.Text.Json.Serialization;

namespace AgentCore.Conversation;

public class Message
{
    [JsonPropertyName("role")]
    public Role Role { get; set; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents { get; set; } = Array.Empty<IContent>();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    [JsonConstructor]
    public Message() { }

    public Message(Role role, IContent content, Dictionary<string, object>? metadata = null)
    {
        Role = role;
        Contents = [content];
        Metadata = metadata ?? new Dictionary<string, object>();
    }

    public Message(Role role, IReadOnlyList<IContent> contents, Dictionary<string, object>? metadata = null)
    {
        Role = role;
        Contents = contents;
        Metadata = metadata ?? new Dictionary<string, object>();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }

