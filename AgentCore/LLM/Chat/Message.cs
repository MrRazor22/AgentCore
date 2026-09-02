using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat; 

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(ToolCall), "toolCall")]
[JsonDerivedType(typeof(ToolResult), "toolResult")]
[JsonDerivedType(typeof(Reasoning), "reasoning")]
public interface IContent 
{ 
    int EstimateTokens();
    IContent Truncate(int maxTokens);
} 

public sealed record MessageMetadata(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null,
    [property: JsonPropertyName("usage")] TokenUsage? Usage = null
);

public class Message
{
    protected readonly List<IContent> _contents = [];

    [JsonPropertyName("role")]
    public Role Role { get; protected set; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    [JsonPropertyName("metadata")]
    public MessageMetadata? Metadata { get; protected set; }

    [JsonConstructor]
    public Message(Role role, IReadOnlyList<IContent>? contents = null, MessageMetadata? metadata = null)
    {
        Role = role;
        if (contents != null)
        {
            _contents.AddRange(contents);
        }
        Metadata = metadata;
    }

    public Message(Role role, IContent content) : this(role, [content], null) { }
    public Message(Role role, params IContent[] contents) : this(role, (IReadOnlyList<IContent>)contents, null) { }
}

