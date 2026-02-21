using AgentCore.Chat;
using AgentCore.Tokens;

namespace AgentCore.LLM.Protocol;

public enum StreamKind { Text, ToolCallDelta, Structured, Usage, Finish }

public readonly struct LLMStreamChunk(StreamKind Kind, object? Payload = null)
{
    public StreamKind Kind { get; } = Kind;
    public object? Payload { get; } = Payload;
}

public static class LLMStreamChunkExtensions
{
    public static FinishReason? AsFinishReason(this LLMStreamChunk chunk) => chunk.Payload as FinishReason?;
    public static TokenUsage? AsTokenUsage(this LLMStreamChunk chunk) => chunk.Payload as TokenUsage;
    public static string? AsText(this LLMStreamChunk chunk) => chunk.Payload as string;
    public static ToolCall? AsToolCall(this LLMStreamChunk chunk) => chunk.Payload as ToolCall;
    public static ToolCallDelta? AsToolCallDelta(this LLMStreamChunk chunk) => chunk.Payload as ToolCallDelta;
}

public class ToolCallDelta
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Delta { get; set; }
}
