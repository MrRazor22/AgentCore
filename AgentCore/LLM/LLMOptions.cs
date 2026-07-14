using AgentCore.LLM.Schema;

namespace AgentCore.LLM;

public sealed class LLMOptions
{
    public string? Model { get; init; }
    public float? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public JsonSchema? ResponseSchema { get; init; }
}
