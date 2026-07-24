using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;

/// <summary>
/// Root interface for any output emitted by an ILLM provider stream.
/// </summary>
public interface ILLMOutput { }

/// <summary>
/// Sub-interface for transient token-level streaming content fragments emitted by ILLM providers.
/// </summary>
public interface IContentDelta : ILLMOutput { }

public record TextDelta(string Value) : IContentDelta;

public record ReasoningDelta(string Thought) : IContentDelta;

public record ToolCallDelta(string Id, string? NameDelta, string? ArgumentsDelta, int? Index = null) : IContentDelta;

public record Metadata(
    int InputTokens = 0,
    int OutputTokens = 0,
    int? ReasoningTokens = null,
    string? FinishReason = null) : ILLMOutput;

/// <summary>
/// Root interface for settled, fully validated semantic content items.
/// Streamed at the Agent boundary, stored in Message objects, and retained in IContext.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(ToolCall), "toolCall")]
[JsonDerivedType(typeof(ToolResult), "toolResult")]
[JsonDerivedType(typeof(Reasoning), "reasoning")]
public interface IContent
{
    string ForLlm();
}

public sealed record Text([property: JsonPropertyName("Value")] string Value) : IContent
{
    public static implicit operator Text(string text) => new(text);
    public string ForLlm() => Value;
}

public sealed record ToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonObject Arguments
) : IContent
{
    public string ForLlm()
    {
        if (Arguments.Count == 0)
            return Name;

        var args = string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"));
        return $"{Name}({args})";
    }
}

public sealed record ToolResult(
    [property: JsonPropertyName("call_id")] string CallId,
    [property: JsonPropertyName("result")] IContent? Result
) : IContent
{
    public string ForLlm()
        => Result?.ForLlm() ?? "";
}

public sealed record Reasoning([property: JsonPropertyName("Thought")] string Thought) : IContent
{
    public string ForLlm() => Thought;
}
