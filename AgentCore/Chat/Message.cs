using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Chat;

public class Message
{
    public Role Role { get; }
    public IContent Content { get; }

    [JsonConstructor]
    private Message(Role role, JsonNode content)
    {
        Role = role;
        Content = content.GetValueKind() == JsonValueKind.String
            ? new TextContent(content.GetValue<string>()!)
            : content is JsonObject obj && obj["Text"] != null
                ? new TextContent(obj["Text"]!.GetValue<string>())
                : throw new Exception("Unknown content type.");
    }

    public Message(Role role, IContent content) { Role = role; Content = content; }
    public Message(Role role, string content) { Role = role; Content = new TextContent(content); }
}
