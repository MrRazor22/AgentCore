using AgentCore.Tokens;
using System.Text.Json.Serialization;

namespace AgentCore.LLM;

public record LLMMeta(FinishReason FinishReason, TokenUsage? TokenUsage);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FinishReason { Stop, ToolCall, Cancelled }

public enum ToolCallMode { None, Auto, Required }