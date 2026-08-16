namespace AgentCore.LLM;

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

public record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0,
    int? ReasoningTokens = null) : ILLMOutput;

public record FinishReason(string Value) : ILLMOutput;
