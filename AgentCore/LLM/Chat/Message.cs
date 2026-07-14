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

    public Message(Role role, IContent content) : this(role, [content]){ }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }

