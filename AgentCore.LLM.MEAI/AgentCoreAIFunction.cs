using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts an AgentCore Tool to a Microsoft.Extensions.AI AIFunction.
/// </summary>
public sealed class AgentCoreAIFunction : AIFunction
{
    private readonly AgentCore.Tools.ToolDefinition _tool;

    public AgentCoreAIFunction(AgentCore.Tools.ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tool = tool;
    }

    public override string Name => _tool.Name;

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _tool.ParametersSchema.ToJsonElement();

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // This function exists only to expose metadata to MEAI. Invocation is handled by AgentCore, not MEAI.
        throw new NotSupportedException("Tool execution is managed by AgentCore's tooling pipeline, not via direct MEAI invocation.");
    }
}
 