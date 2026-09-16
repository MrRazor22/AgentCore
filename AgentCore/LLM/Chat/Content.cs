using AgentCore.LLM;
namespace AgentCore.LLM.Chat;

public class Text(string value) : IContent
{
    public string Value { get; } = value ?? "";
    public static implicit operator Text(string text) => new(text);
    public override string ToString() => Value;
}

public class Reasoning(string thought) : IContent
{
    public string Thought { get; } = thought ?? "";
    public override string ToString() => Thought;
}

public class ToolCall(string id, string name, string? arguments = null) : IContent
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Arguments { get; } = arguments ?? string.Empty;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Arguments) ? Name : $"{Name}({Arguments})";
}

public record Image(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "image/png",
    int? Width = null,
    int? Height = null) : IContent;