using System.Text.Json.Serialization;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat;

public sealed record MessageMetadata(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null,
    [property: JsonPropertyName("usage")] TokenUsage? Usage = null
);

public class Message
{
    private readonly List<IContent> _contents;

    [JsonPropertyName("role")]
    public Role Role { get; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    [JsonPropertyName("metadata")]
    public MessageMetadata? Metadata { get; }

    public Message(Role role, IReadOnlyList<IContent>? contents = null, MessageMetadata? metadata = null)
    {
        Role = role;
        _contents = contents != null ? [.. contents] : [];
        Metadata = metadata;
    }

    public Message(Role role, IContent content) : this(role, [content], null) { }
    public Message(Role role, params IContent[] contents) : this(role, (IReadOnlyList<IContent>)contents, null) { }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }
