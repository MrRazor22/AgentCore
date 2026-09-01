using System.Text.Json;
using AgentCore.Tools;
using Microsoft.Extensions.AI;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts an AgentCore ToolDefinition to a Microsoft.Extensions.AI AIFunction.
/// </summary>
public sealed class AgentCoreAIFunction(ToolDefinition tool) : AIFunction
{
    private readonly ToolDefinition _tool = tool ?? throw new ArgumentNullException(nameof(tool));

    public override string Name => _tool.Name;

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _tool.ParametersSchema.ToJsonElement();

    protected override Task<object?> InvokeCoreAsync(
        IEnumerable<KeyValuePair<string, object?>> arguments,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Tool execution is managed by AgentCore's tooling pipeline, not via direct MEAI invocation.");
    }
}
