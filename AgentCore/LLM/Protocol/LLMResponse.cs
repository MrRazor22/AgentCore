using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Utils;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Protocol;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FinishReason { Stop, ToolCall, Cancelled }

public sealed class LLMResponse
{
    private TokenUsage? _tokenUsage;

    public string? Text { get; internal set; }
    public ToolCall? ToolCall { get; internal set; }
    public object? Output { get; set; }
    public FinishReason FinishReason { get; internal set; }

    public TokenUsage? TokenUsage
    {
        get => _tokenUsage;
        internal set
        {
            if (_tokenUsage != null) throw new InvalidOperationException("TokenUsage already set");
            _tokenUsage = value;
        }
    }

    public bool HasToolCall => ToolCall != null;

    public string ToCountablePayload()
        => string.Concat(Text.AsJsonString(), Output?.AsJsonString(), ToolCall?.AsJsonString());
}
