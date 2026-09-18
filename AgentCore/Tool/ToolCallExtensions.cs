using AgentCore.LLM.Chat;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tool;

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
