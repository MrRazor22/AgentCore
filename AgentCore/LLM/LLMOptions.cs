using AgentCore.Schema;

namespace AgentCore.LLM;

public sealed class LLMOptions
{
    public float? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public JsonSchema? ResponseSchema { get; init; }
}
