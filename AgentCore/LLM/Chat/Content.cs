using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;



/// <summary>
/// Root interface for settled, fully validated semantic content items.
/// Streamed at the Agent boundary, stored in Message objects, and retained in IContext.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(ToolCall), "toolCall")]
[JsonDerivedType(typeof(ToolResult), "toolResult")]
[JsonDerivedType(typeof(Reasoning), "reasoning")]
[JsonDerivedType(typeof(AgentCore.Context.CompactedSummary), "compactedSummary")]
public interface IContent
{
    string ForLlm();

    /// <summary>
    /// Determines whether this content item can be consolidated with another incoming content item.
    /// </summary>
    bool CanConsolidateWith(IContent other) => false;

    /// <summary>
    /// Consolidates this content item with another compatible incoming content item into a single content item.
    /// </summary>
    IContent Consolidate(IContent other) => throw new NotSupportedException($"Consolidation is not supported for {GetType().Name}.");
}

public sealed record Text([property: JsonPropertyName("Value")] string Value) : IContent
{
    public static implicit operator Text(string text) => new(text);
    public string ForLlm() => Value;

    public bool CanConsolidateWith(IContent other) => other is Text;

    public IContent Consolidate(IContent other) =>
        other is Text t
            ? new Text(Value + t.Value)
            : throw new InvalidOperationException("Cannot consolidate Text with non-Text content.");
}

public sealed record ToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonObject Arguments
) : IContent
{
    internal int? Index { get; init; }
    internal string RawArguments { get; init; } = "";

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

    public bool CanConsolidateWith(IContent other) => other is Reasoning;

    public IContent Consolidate(IContent other) =>
        other is Reasoning r
            ? new Reasoning(Thought + r.Thought)
            : throw new InvalidOperationException("Cannot consolidate Reasoning with non-Reasoning content.");
}

