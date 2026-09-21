using AgentCore.LLM;
using System.Text.Json;
using System.Text.Json.Nodes;
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
public static class ToolCallExtensions
{
    public static (JsonObject? Args, string? Error) ParseArguments(this ToolCall call)
    {
        if (string.IsNullOrWhiteSpace(call.Arguments)) return ([], null);
        try
        {
            return JsonNode.Parse(call.Arguments) is JsonObject obj
                ? (obj, null)
                : (null, $"Tool arguments must be a JSON object, got non-object payload: '{call.Arguments}'.");
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON ({ex.Message}). Raw payload: '{call.Arguments}'.");
        }
    }
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

public record Audio(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "audio/wav",
    TimeSpan? Duration = null) : IContent, IToolResultContent;

public record Video(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "video/mp4",
    int? Width = null,
    int? Height = null,
    TimeSpan? Duration = null) : IContent, IToolResultContent;