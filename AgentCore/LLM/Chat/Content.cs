using AgentCore.LLM;
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

public class ToolCall(string id, string name, string? arguments = null) : IContent
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public virtual string Arguments { get; } = arguments ?? string.Empty;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Arguments) ? Name : $"{Name}({Arguments})";
}

public record Image(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "image/png",
    int? Width = null,
    int? Height = null) : IContent;