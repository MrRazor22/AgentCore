using AgentCore.LLM.Schema;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts a Microsoft.Extensions.AI AIFunction to an AgentCore Tool.
/// </summary>
public sealed class MEAIFunctionTool : AgentCore.Tools.Tool
{
    private readonly AIFunction _aiFunction;

    public MEAIFunctionTool(AIFunction aiFunction)
        : base(
            GetFunctionName(aiFunction),
            GetFunctionDescription(aiFunction),
            GetFunctionSchema(aiFunction))
    {
        _aiFunction = aiFunction;
    }

    public override async Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // Convert the JsonObject arguments back to AIFunctionArguments
        var argumentsJson = arguments.ToJsonString();
        var aiArgs = JsonSerializer.Deserialize<AIFunctionArguments>(argumentsJson) ?? new AIFunctionArguments();

        return await _aiFunction.InvokeAsync(aiArgs, ct).ConfigureAwait(false);
    }

    private static string GetFunctionName(AIFunction aiFunction)
    {
        ArgumentNullException.ThrowIfNull(aiFunction);
        return aiFunction.Name;
    }

    private static string GetFunctionDescription(AIFunction aiFunction)
    {
        ArgumentNullException.ThrowIfNull(aiFunction);
        return aiFunction.Description;
    }

    private static JsonSchema GetFunctionSchema(AIFunction aiFunction)
    {
        ArgumentNullException.ThrowIfNull(aiFunction);
        var schemaJson = aiFunction.JsonSchema.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : aiFunction.JsonSchema.GetRawText();

        var node = JsonNode.Parse(schemaJson)?.AsObject() ?? new JsonObject();
        return new JsonSchema(node);
    }
}
