namespace AgentCore.LLM;

/// <summary>
/// Root interface for any output emitted by an ILLM provider stream.
/// </summary>
public interface ILLMOutput { }

// 1. Text Streaming
public sealed record TextDelta(string Text) : ILLMOutput;
public sealed record TextEnd() : ILLMOutput;

// 2. Reasoning / Thinking Streaming
public sealed record ReasoningDelta(string Thought) : ILLMOutput;
public sealed record ReasoningEnd() : ILLMOutput;

// 3. Tool Call Streaming (LLMs provide real IDs for parallel tool calls)
public sealed record ToolCallStart(string Id, string Name, int? Index = null) : ILLMOutput;
public sealed record ToolCallDelta(string Id, string Arguments, int? Index = null) : ILLMOutput;
public sealed record ToolCallEnd(string Id, int? Index = null) : ILLMOutput;

// 4. Telemetry & Turn Completion
public sealed record TokenUsage(
    int InputTokens = 0,
    int OutputTokens = 0,
    int? ReasoningTokens = null) : ILLMOutput;

public sealed record FinishReason(string Value) : ILLMOutput;
