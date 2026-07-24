using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts an AgentCore Tool to a Microsoft.Extensions.AI AIFunction.
/// </summary>
public sealed class AgentCoreAIFunction : AIFunction
{
    private readonly AgentCore.Tools.Tool _tool;

    public AgentCoreAIFunction(AgentCore.Tools.Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tool = tool;
    }

    public override string Name => _tool.Name;

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _tool.ParametersSchema.ToJsonElement();

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Convert AIFunctionArguments to JsonObject for AgentCore tool execution
        var jsonNode = JsonSerializer.SerializeToNode(arguments);
        var jsonObject = jsonNode?.AsObject() ?? new JsonObject();
        return await _tool.InvokeAsync(jsonObject, cancellationToken).ConfigureAwait(false);
    }
}
