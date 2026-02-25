using AgentCore.Tooling;

namespace AgentCore.LLM;

public sealed record LLMOptions(
    string? Model = null,
    string? ApiKey = null,
    string? BaseUrl = null,
    ToolCallMode ToolCallMode = ToolCallMode.Auto,
    int? ContextLength = null,
    float? Temperature = null,
    float? TopP = null,
    int? MaxOutputTokens = null,
    int? Seed = null,
    IReadOnlyList<string>? StopSequences = null,
    float? FrequencyPenalty = null,
    float? PresencePenalty = null,
    float? TopK = null,
    Type? OutputType = null
);
