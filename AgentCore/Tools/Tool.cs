using AgentCore.LLM;
using AgentCore.LLM.Schema;
using System.Text.Json.Nodes;

namespace AgentCore.Tools; 

public sealed record ToolDefinition(string Name, string Description, JsonSchema ParametersSchema);

public interface ITool
{
    ToolDefinition Definition { get; }
    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(JsonObject arguments, CancellationToken ct = default);
}