using System.Text.Json.Nodes;
using AgentCore.Tooling;

namespace AgentCore.LLM;

public sealed class LLMOptions
{
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public ToolCallMode ToolCallMode { get; set; } = ToolCallMode.Auto;
    public int? ContextLength { get; set; }
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? Seed { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public float? TopK { get; set; }
    public JsonObject? ResponseSchema { get; set; }
}
