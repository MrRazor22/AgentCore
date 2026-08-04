using AgentCore.LLM.Schema;

namespace AgentCore.Tools;

public sealed record ToolDefinition
{
    public string Name { get; }
    public string Description { get; }
    public JsonSchema ParametersSchema { get; }

    public ToolDefinition(string name, string description, JsonSchema parametersSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(parametersSchema);

        Name = name;
        Description = description;
        ParametersSchema = parametersSchema;
    }
}
