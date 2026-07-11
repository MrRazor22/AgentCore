namespace AgentCore.LLM;

public sealed class LLMOptions
{
    public ToolCallMode ToolCallMode { get; set; } = ToolCallMode.Auto;
    public float? Temperature { get; set; }
    public int? MaxOutputTokens { get; set; }
}
