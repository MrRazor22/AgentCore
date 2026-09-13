using AgentCore.LLM.Chat;

namespace AgentCore;

public interface IAgentEvent;

// Message envelope events
public interface IMessageEvent : IAgentEvent { string? MessageId { get; } } 
public sealed record MessageStart(Role Role = Role.Assistant, string? MessageId = null) : IMessageEvent;
public sealed record MessageEnd(string? MessageId = null) : IMessageEvent;
public sealed record MetadataEvent(IMetadata Metadata, string? MessageId = null) : IMessageEvent;

// Multiplexing Envelope for concurrent streams on a shared channel
public sealed record MessageEvent(string MessageId, IAgentEvent Event) : IAgentEvent, IMessageEvent;

// Universal Block events
public interface IBlockEvent : IAgentEvent { int Index { get; } }
public interface IBlockStartEvent : IBlockEvent;
public interface IBlockDeltaEvent : IBlockEvent;
public interface IBlockEndEvent : IBlockEvent;

// Complete Content Block (for non-streaming or multimodal content like Image/Text)
public sealed record ContentEvent(int Index, IContent Content) : IBlockEvent;

// Text Block
public sealed record TextStart(int Index = 0) : IBlockStartEvent;
public sealed record TextDelta(int Index, string Text) : IBlockDeltaEvent;
public sealed record TextEnd(int Index = 0) : IBlockEndEvent; 

// Reasoning Block (LLM)
public sealed record ReasoningStart(int Index = 0) : IBlockStartEvent;
public sealed record ReasoningDelta(int Index, string Thought) : IBlockDeltaEvent;
public sealed record ReasoningEnd(int Index = 0) : IBlockEndEvent;

// Tool Call Block (LLM)
public sealed record ToolCallStart(int Index, string Id, string Name) : IBlockStartEvent;
public sealed record ToolCallDelta(int Index, string Arguments) : IBlockDeltaEvent;
public sealed record ToolCallEnd(int Index = 0) : IBlockEndEvent; 



