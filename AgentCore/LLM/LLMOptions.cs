using System.Text.Json.Nodes;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace AgentCore.LLM;

public enum ReasoningEffort { None, Low, Medium, High }

public sealed class LLMOptions
{
    public ToolCallMode ToolCallMode { get; set; } = ToolCallMode.Auto;
    public int? ContextWindow { get; set; }
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? Seed { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public JsonSchema? ResponseSchema { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public int MaxRetries { get; set; } = 3;
}
