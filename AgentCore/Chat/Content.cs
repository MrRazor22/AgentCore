using System.Text.Json.Nodes;
namespace AgentCore.Chat;

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
    string Id,
    string Name,
    JsonObject Arguments
) : IContent
{
    public static ToolCall Create(
        string name,
        JsonObject? arguments = null,
        string? id = null)
        => new(id ?? Guid.NewGuid().ToString(), name, arguments ?? new());

    public string ForLlm()
    {
        if (Arguments.Count == 0)
            return Name;

        var args = string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"));
        return $"{Name}({args})";
    }
}

public sealed record ToolResult(
    string CallId,
    object? Result
) : IContent
{
    public string ForLlm()
        => Result?.ToString() ?? "";
}
