using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;

public interface IAgentResponse { }

/// <summary>
/// Root interface for any output emitted by an ILLM provider stream.
/// </summary>
public interface ILLMOutput { }

/// <summary>
/// Sub-interface for transient token-level streaming content fragments emitted by ILLM providers.
/// </summary>
public interface IContentDelta : ILLMOutput, IAgentResponse
{
    void AccumulateInto(List<IContent> contents);
}

public record TextDelta(string Value) : IContentDelta
{
    public void AccumulateInto(List<IContent> contents)
    {
        if (contents.Count > 0 && contents[^1] is Text last)
        {
            contents[^1] = new Text(last.Value + Value);
        }
        else
        {
            contents.Add(new Text(Value));
        }
    }
}

public record ReasoningDelta(string Thought) : IContentDelta
{
    public void AccumulateInto(List<IContent> contents)
    {
        if (contents.Count > 0 && contents[^1] is Reasoning last)
        {
            contents[^1] = new Reasoning(last.Thought + Thought);
        }
        else
        {
            contents.Add(new Reasoning(Thought));
        }
    }
}

public record ToolCallDelta(string Id, string? NameDelta, string? ArgumentsDelta, int? Index = null) : IContentDelta
{
    public void AccumulateInto(List<IContent> contents)
    {
        ToolCall? existing = null;
        int existingIndex = -1;

        for (int i = 0; i < contents.Count; i++)
        {
            if (contents[i] is ToolCall tc)
            {
                if (Index.HasValue)
                {
                    if (tc.Index == Index.Value || (!string.IsNullOrEmpty(Id) && tc.Id == Id))
                    {
                        existing = tc;
                        existingIndex = i;
                        break;
                    }
                }
                else if (!string.IsNullOrEmpty(Id))
                {
                    if (tc.Id == Id)
                    {
                        existing = tc;
                        existingIndex = i;
                        break;
                    }
                }
            }
        }

        if (existing == null && !Index.HasValue && string.IsNullOrEmpty(Id))
        {
            var allToolCalls = contents.OfType<ToolCall>().ToList();
            if (allToolCalls.Count > 1)
                throw new InvalidOperationException("Ambiguous tool call delta: multiple active tool calls exist.");
            if (allToolCalls.Count == 1)
            {
                existing = allToolCalls[0];
                existingIndex = contents.IndexOf(existing);
            }
        }

        var accumulatedId = !string.IsNullOrEmpty(Id) ? Id : (existing?.Id ?? "");
        var accumulatedName = existing?.Name ?? "";
        if (!string.IsNullOrEmpty(NameDelta))
        {
            if (string.IsNullOrEmpty(accumulatedName) || (accumulatedName != NameDelta && !accumulatedName.EndsWith(NameDelta)))
            {
                accumulatedName += NameDelta;
            }
        }

        var accumulatedRawArgs = (existing?.RawArguments ?? "") + (ArgumentsDelta ?? "");
        JsonObject? parsedArgs = null;
        if (!string.IsNullOrEmpty(accumulatedRawArgs))
        {
            try
            {
                parsedArgs = JsonNode.Parse(accumulatedRawArgs)?.AsObject();
            }
            catch { }
        }

        var updated = new ToolCall(accumulatedId, accumulatedName, parsedArgs ?? new JsonObject())
        {
            Index = Index ?? existing?.Index,
            RawArguments = accumulatedRawArgs
        };

        if (existingIndex >= 0)
        {
            contents[existingIndex] = updated;
        }
        else
        {
            contents.Add(updated);
        }
    }
}

public record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0,
    int? ReasoningTokens = null) : ILLMOutput, IAgentResponse;

public record FinishReason(string Value) : ILLMOutput, IAgentResponse;

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
public interface IContent : IAgentResponse
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
}
