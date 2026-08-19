namespace AgentCore.LLM;

/// <summary>
/// Root interface for any output emitted by an ILLM provider stream.
/// </summary>
public interface ILLMOutput { }

/// <summary>
/// Sub-interface for transient token-level streaming content fragments emitted by ILLM providers.
/// </summary>
public interface IContentDelta : ILLMOutput
{
    string? Id { get; }
    int? Index { get; }
    bool IsFinal { get; }
}

public record TextDelta(string Value, string? Id = null, int? Index = null, bool IsFinal = false) : IContentDelta;

public record ReasoningDelta(string Thought, string? Id = null, int? Index = null, bool IsFinal = false) : IContentDelta;

public record ToolCallDelta(string Id, string? NameDelta, string? ArgumentsDelta, int? Index = null, bool IsFinal = false) : IContentDelta;

public record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0,
    int? ReasoningTokens = null) : ILLMOutput;

public record FinishReason(string Value) : ILLMOutput;

