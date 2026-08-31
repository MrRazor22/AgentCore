using AgentCore.LLM.Chat;

namespace AgentCore.LLM;

public interface IMessageEvent;
public interface IBlockEvent : IMessageEvent { int Index { get; } }
public interface IBlockStartEvent : IBlockEvent { IStreamingContent CreateStream(); }
public interface IDataDeltaEvent<out T> : IBlockEvent { T Data { get; } }
public interface IBlockEndEvent : IBlockEvent;

// Envelope Events
public sealed record MessageStart(Role Role = Role.Assistant, string? Id = null, string? Model = null) : IMessageEvent;
public sealed record MessageEnd(string? FinishReason = null, TokenUsage? Usage = null) : IMessageEvent;

// Text Block
public sealed record TextStart(int Index = 0) : IBlockStartEvent { public IStreamingContent CreateStream() => new Text.Stream(); }
public sealed record TextDelta(int Index, string Text) : IDataDeltaEvent<string> { public string Data => Text; }
public sealed record TextEnd(int Index = 0) : IBlockEndEvent;

// Reasoning Block
public sealed record ReasoningStart(int Index = 0) : IBlockStartEvent { public IStreamingContent CreateStream() => new Reasoning.Stream(); }
public sealed record ReasoningDelta(int Index, string Thought) : IDataDeltaEvent<string> { public string Data => Thought; }
public sealed record ReasoningEnd(int Index = 0) : IBlockEndEvent;

// Tool Call Block
public sealed record ToolCallStart(int Index, string Id, string Name) : IBlockStartEvent { public IStreamingContent CreateStream() => new ToolCall.Stream(Id, Name); }
public sealed record ToolCallDelta(int Index, string Arguments) : IDataDeltaEvent<string> { public string Data => Arguments; }
public sealed record ToolCallEnd(int Index = 0) : IBlockEndEvent;

// Telemetry
public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
