namespace AgentCore.LLM;

/// <summary>
/// Root interface for any output emitted by an ILLM provider stream.
/// </summary>
public interface ILLMOutput { }

/// <summary>
/// Marker interface for the actual inner payload (text, tool call, reasoning, audio, image, etc.).
/// </summary>
public interface IContentChunk { }

/// <summary>
/// Stream envelope holding streaming metadata and the actual content chunk.
/// </summary>
public record StreamChunk(
    IContentChunk Content,
    int? Index = null,
    string? Id = null,
    bool IsFinal = false
) : ILLMOutput;

public record TextChunk(string Text) : IContentChunk;

public record ReasoningChunk(string Thought) : IContentChunk;

public record ToolCallChunk(
    string? Name = null,
    string? Arguments = null
) : IContentChunk;

public record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0,
    int? ReasoningTokens = null) : ILLMOutput;

public record FinishReason(string Value) : ILLMOutput;

