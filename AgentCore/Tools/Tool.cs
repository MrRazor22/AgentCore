using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Tooling;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute(string? name = null, string? description = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Description { get; } = description;
}

public class Tool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required JsonObject ParametersSchema { get; set; }

    [JsonIgnore]
    public Delegate? Function { get; set; }

    public override string ToString()
    {
        var props = ParametersSchema?["properties"] as JsonObject;
        var args = props != null ? string.Join(", ", props.Select(p => p.Key)) : "";
        var argPart = args.Length > 0 ? $"({args})" : "()";
        return !string.IsNullOrWhiteSpace(Description) ? $"{Name}{argPart} => {Description}" : $"{Name}{argPart}";
    }
}
