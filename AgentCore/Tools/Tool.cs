using AgentCore.LLM.Schema;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public abstract class Tool
{
    public string Name { get; }
    public string Description { get; }
    public JsonSchema ParametersSchema { get; }

    protected Tool(string name, string description, JsonSchema parametersSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(parametersSchema);

        Name = name;
        Description = description;
        ParametersSchema = parametersSchema;
    }

    public abstract Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct);

    public override string ToString()
    {
        var args = string.Join(", ", ParametersSchema.ParameterNames);
        var argPart = args.Length > 0 ? $"({args})" : "()";
        return !string.IsNullOrWhiteSpace(Description) ? $"{Name}{argPart} => {Description}" : $"{Name}{argPart}";
    }
}
