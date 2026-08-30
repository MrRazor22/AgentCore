using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;

internal interface IContentAccumulator
{
    IContent Complete();
}

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
}

public sealed record Text([property: JsonPropertyName("Value")] string Value) : IContent
{
    public static implicit operator Text(string text) => new(text);
    public string ForLlm() => Value;

    internal sealed class Accumulator : IContentAccumulator
    {
        private readonly StringBuilder _sb = new();
        public void Append(string chunk) => _sb.Append(chunk);
        public IContent Complete() => new Text(_sb.ToString());
    }
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

    internal sealed class Accumulator(string id, string name) : IContentAccumulator
    {
        private readonly StringBuilder _args = new();
        public void Append(string chunk) => _args.Append(chunk);

        public IContent Complete()
        {
            var raw = _args.ToString();
            JsonObject? args = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    args = JsonNode.Parse(raw)?.AsObject();
                }
                catch (Exception ex)
                {
                    throw new FormatException($"Malformed JSON arguments for tool '{name}' (id: '{id}'): {raw}", ex);
                }
            }

            return new ToolCall(id, name, args ?? new JsonObject());
        }
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

    internal sealed class Accumulator : IContentAccumulator
    {
        private readonly StringBuilder _sb = new();
        public void Append(string chunk) => _sb.Append(chunk);
        public IContent Complete() => new Reasoning(_sb.ToString());
    }
}


