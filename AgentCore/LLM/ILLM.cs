using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.LLM;

public interface ILLM
{
    IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default);
}

public interface IMessageEvent;
public interface IBlockEvent : IMessageEvent { int Index { get; } }
public interface IBlockStartEvent : IBlockEvent { IStreamingContent CreateStream(); }
public interface IBlockDeltaEvent : IBlockEvent;
public interface IBlockEndEvent : IBlockEvent;

// Envelope Events
public sealed record MessageStart(Role Role = Role.Assistant, string? Id = null, string? Model = null) : IMessageEvent;
public sealed record MessageEnd(string? FinishReason = null, TokenUsage? Usage = null) : IMessageEvent;

// Text Block
public sealed record TextStart(int Index = 0) : IBlockStartEvent { public IStreamingContent CreateStream() => new StreamingText(); }
public sealed record TextDelta(int Index, string Text) : IBlockDeltaEvent;
public sealed record TextEnd(int Index = 0) : IBlockEndEvent;

// Reasoning Block
public sealed record ReasoningStart(int Index = 0) : IBlockStartEvent { public IStreamingContent CreateStream() => new StreamingReasoning(); }
public sealed record ReasoningDelta(int Index, string Thought) : IBlockDeltaEvent;
public sealed record ReasoningEnd(int Index = 0) : IBlockEndEvent;

// Tool Call Block
public sealed record ToolCallStart(int Index, string Id, string Name) : IBlockStartEvent { public IStreamingContent CreateStream() => new StreamingToolCall(Id, Name); }
public sealed record ToolCallDelta(int Index, string Arguments) : IBlockDeltaEvent;
public sealed record ToolCallEnd(int Index = 0) : IBlockEndEvent;

// Telemetry
public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

