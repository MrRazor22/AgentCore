using AgentCore.Tokens;
using System.Text.Json.Serialization;

namespace AgentCore.LLM;


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FinishReason { Stop, ToolCall, Cancelled }

public enum ToolCallMode { None, Auto, Required }