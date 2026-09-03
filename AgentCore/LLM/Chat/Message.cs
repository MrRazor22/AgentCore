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
    IContent Truncate(int maxTokens, string? notice = null);
} 

public sealed record MessageMetadata(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null,
    [property: JsonPropertyName("usage")] TokenUsage? Usage = null
);

public class Message(
    Role role,
    IReadOnlyList<IContent>? contents = null,
    MessageMetadata? metadata = null)
{
    protected readonly List<IContent> _contents = contents != null ? [.. contents] : [];

    [JsonPropertyName("role")]
    public Role Role { get; protected set; } = role;

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    [JsonPropertyName("metadata")]
    public MessageMetadata? Metadata { get; protected set; } = metadata;
}

