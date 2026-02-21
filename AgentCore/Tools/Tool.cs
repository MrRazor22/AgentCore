using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentCore.Tools;

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
    public required JObject ParametersSchema { get; set; }

    [JsonIgnore]
    public Delegate? Function { get; set; }

    public override string ToString()
    {
        var props = ParametersSchema?["properties"] as JObject;
        var args = props != null ? string.Join(", ", props.Properties().Select(p => p.Name)) : "";
        var argPart = args.Length > 0 ? $"({args})" : "()";
        return !string.IsNullOrWhiteSpace(Description) ? $"{Name}{argPart} => {Description}" : $"{Name}{argPart}";
    }
}
