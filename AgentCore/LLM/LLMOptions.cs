using System.Text.Json.Nodes;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace AgentCore.LLM;

public enum ReasoningEffort { None, Low, Medium, High }

public sealed class LLMOptions
{
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public Uri? BaseUrl { get; set; }
    public ToolCallMode ToolCallMode { get; set; } = ToolCallMode.Auto;
    public TokenBudget? ContextWindow { get; set; }
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public TokenBudget? MaxOutputTokens { get; set; }
    public int? Seed { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public JsonSchema? ResponseSchema { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public int MaxRetries { get; set; } = 3;
}
