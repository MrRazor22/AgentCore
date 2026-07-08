using AgentCore.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute(string? name = null, string? description = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Description { get; } = description;

    /// <summary>
    /// Whether this tool requires user approval before execution (Letta-style)
    /// </summary>
    public bool RequiresApproval { get; set; } = false;
}

public abstract class Tool
{
    public string Name { get; }
    public string Description { get; }
    public JsonSchema ParametersSchema { get; }
    public bool RequiresApproval { get; }

    /// <summary>
    /// A diagnostic identifier describing where the tool originated (e.g. WeatherTools.GetWeather, mcp://filesystem/read_file).
    /// </summary>
    public string Source { get; }

    protected Tool(string name, string description, JsonSchema parametersSchema, bool requiresApproval, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(parametersSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        Name = name;
        Description = description;
        ParametersSchema = parametersSchema;
        RequiresApproval = requiresApproval;
        Source = source;
    }

    public abstract Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct);

    public override string ToString()
    {
        var args = string.Join(", ", ParametersSchema.ParameterNames);
        var argPart = args.Length > 0 ? $"({args})" : "()";
        return !string.IsNullOrWhiteSpace(Description) ? $"{Name}{argPart} => {Description}" : $"{Name}{argPart}";
    }
}
