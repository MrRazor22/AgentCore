using AgentCore.LLM;
namespace AgentCore.LLM.Chat;

public interface IToolResultContent : IContent, IToolResultContentEvent;

public class Text(string value) : IContent, IToolResultContent
{
    public string Value { get; } = value ?? "";
    public static implicit operator Text(string text) => new(text);
}

public class Reasoning(string thought) : IContent
{
    public string Thought { get; } = thought ?? "";
}

public class ToolCall(string id, string name, string? arguments = null) : IContent
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Arguments { get; } = arguments ?? string.Empty;
}

public class ToolResult(string toolCallId, IReadOnlyList<IToolResultContent> contents, bool isError = false) : IContent
{
    public string ToolCallId { get; } = toolCallId;
    public IReadOnlyList<IToolResultContent> Contents { get; } = contents ?? [];
    public bool IsError { get; } = isError;
}

public record Image(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "image/png",
    int? Width = null,
    int? Height = null) : IContent, IToolResultContent;