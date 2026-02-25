using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tools;
using AgentCore.Utils;

namespace AgentCore.LLM.Protocol;

public enum ToolCallMode { None, Auto, Required }

public sealed class LLMGenerationOptions
{
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? Seed { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
    public IDictionary<int, int>? LogitBias { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public float? TopK { get; set; }
}

public sealed class LLMRequest(
    IList<Message> Prompt,
    ToolCallMode ToolCallMode = ToolCallMode.Auto,
    string? Model = null,
    LLMGenerationOptions? Options = null
)
{
    public IList<Message> Prompt { get; internal set; } = Prompt;
    public ToolCallMode ToolCallMode { get; } = ToolCallMode;
    public string? Model { get; } = Model;
    public LLMGenerationOptions? Options { get; } = Options;
    public IEnumerable<Tool>? AvailableTools { get; set; }
    public Type? OutputType { get; set; }

    public string ToCountablePayload()
        => string.Concat(
            Prompt.GetSerializableMessages(MessageKinds.All).AsJsonString(),
            AvailableTools.AsJsonString(),
            OutputType?.GetSchemaForType().AsJsonString()
        );

    public LLMRequest Clone() => (LLMRequest)MemberwiseClone();
}
