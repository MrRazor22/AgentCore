using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public interface ITool
{
    ToolDefinition Definition { get; }
    Task<IReadOnlyList<IContent>> InvokeAsync(JsonObject arguments, CancellationToken ct = default);
}

public sealed record ToolDefinition(string Name, string Description, JsonSchema ParametersSchema);
