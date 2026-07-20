using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(ToolCall), "toolCall")]
[JsonDerivedType(typeof(ToolResult), "toolResult")]
[JsonDerivedType(typeof(Reasoning), "reasoning")]
public interface IContent
{
    string ForLlm();
}

public sealed record Text(string Value) : LLMEvent, IContent
{
    public static implicit operator Text(string text) => new(text);
    public string ForLlm() => Value;
}

public sealed record ToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonNode Arguments,
    [property: JsonIgnore] int? Index = null
) : LLMEvent, IContent
{
    [JsonIgnore]
    public JsonObject ArgumentsObject
    {
        get
        {
            if (Arguments is JsonObject obj) return obj;
            if (Arguments is JsonValue val && val.TryGetValue<string>(out var str))
            {
                try
                {
                    return JsonNode.Parse(str)?.AsObject() ?? new JsonObject();
                }
                catch
                {
                    return new JsonObject();
                }
            }
            return new JsonObject();
        }
    }

    public string ForLlm()
    {
        if (Arguments is JsonObject obj)
        {
            if (obj.Count == 0)
                return Name;

            var args = string.Join(", ", obj.Select(p => $"{p.Key}: {p.Value}"));
            return $"{Name}({args})";
        }
        return $"{Name}({Arguments})";
    }
}

public sealed record ToolResult(
    [property: JsonPropertyName("call_id")] string CallId,
    [property: JsonPropertyName("result")] IContent? Result
) : IContent
{
    public string ForLlm()
        => Result?.ForLlm() ?? "";
}

public sealed record Reasoning(string Thought) : LLMEvent, IContent
{
    public string ForLlm() => Thought;
}
