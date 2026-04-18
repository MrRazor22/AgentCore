using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

public class Tool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required JsonObject ParametersSchema { get; set; }

    /// <summary>
    /// Whether this tool requires user approval before execution (Letta-style)
    /// </summary>
    public bool RequiresApproval { get; set; } = false;

    [JsonIgnore]
    public MethodInfo? Method { get; set; }

    [JsonIgnore]
    public Func<object?[], Task<object?>>? Invoker { get; set; }

    public override string ToString()
    {
        var props = ParametersSchema?["properties"] as JsonObject;
        var args = props != null ? string.Join(", ", props.Select(p => p.Key)) : "";
        var argPart = args.Length > 0 ? $"({args})" : "()";
        return !string.IsNullOrWhiteSpace(Description) ? $"{Name}{argPart} => {Description}" : $"{Name}{argPart}";
    }
}
