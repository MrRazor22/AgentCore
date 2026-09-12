using AgentCore.LLM;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

public class Text(string value) : IContent
{
    public virtual string Value { get; } = value ?? "";
    public static implicit operator Text(string text) => new(text);
    public override string ToString() => Value;
}

public class Reasoning(string value) : IContent
{
    public virtual string Value { get; } = value ?? "";
    public string Thought => Value;
    public override string ToString() => Value;
}

public class ToolCall(string id, string name, JsonObject? arguments = null) : IContent
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public virtual JsonObject Arguments { get; } = arguments ?? new JsonObject();

    public override string ToString() =>
        Arguments.Count == 0 ? Name : $"{Name}({string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"))})";
}

public record Image(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "image/png",
    int? Width = null,
    int? Height = null) : IContent;