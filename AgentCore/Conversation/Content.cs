using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace AgentCore.Conversation;

public interface IContent
{
    string ForLlm();
}

public sealed record Text(string Value) : IContent
{
    public static implicit operator Text(string text) => new(text);
    public string ForLlm() => Value;
}

public sealed record ToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonObject Arguments,
    [property: JsonPropertyName("is_approved")] bool IsApproved = false
) : IContent
{

    public string ForLlm()
    {
        if (Arguments.Count == 0)
            return Name;

        var args = string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"));
        return $"{Name}({args})";
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

public sealed record Reasoning(string Thought) : IContent
{
    public string ForLlm() => Thought;
}
