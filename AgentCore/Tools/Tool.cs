using AgentCore.LLM.Schema;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public abstract class Tool
{
    public ToolDefinition Definition { get; }

    protected Tool(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    public abstract Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct);

    public override string ToString()
    {
        var args = string.Join(", ", Definition.ParametersSchema.ParameterNames);
        var argPart = args.Length > 0 ? $"({args})" : "()";
        return !string.IsNullOrWhiteSpace(Definition.Description) ? $"{Definition.Name}{argPart} => {Definition.Description}" : $"{Definition.Name}{argPart}";
    }
}
