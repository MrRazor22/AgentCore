using AgentCore.LLM.Chat;

namespace AgentCore;

public interface IAgentEvent;

// Message envelope events
public interface IMessageEvent : IAgentEvent { string? MessageId { get; } } 
public sealed record MessageStart(
    Role Role = Role.Assistant,
    string? MessageId = null,
    IReadOnlyList<IMetadata>? Metadata = null
) : IMessageEvent
{
    public string? Id => MessageId;
    public MessageStart(Role role, string? id, string? model)
        : this(role, id, model != null ? [new MessageMetadata(id, model)] : (id != null ? [new MessageMetadata(id)] : null)) { }
}
public sealed record MessageEnd(
    string? FinishReason = null,
    TokenUsage? Usage = null,
    string? MessageId = null,
    IReadOnlyList<IMetadata>? Metadata = null
) : IMessageEvent;


// Universal Block events
public interface IBlockEvent : IAgentEvent
{
    int Index { get; }
    string? MessageId { get; }
}

// Complete Content Block (for non-streaming or multimodal content like Image/Text)
public sealed record ContentBlock(int Index, IContent Content, string? MessageId = null) : IBlockEvent
{
    public string? Id => Content.Id;
    public ContentBlock(IContent content, string? messageId = null) : this(0, content, messageId) { }
}

public interface IBlockStartEvent : IBlockEvent { string? Id { get; } } 
public interface IBlockDeltaEvent : IBlockEvent;
public interface IBlockEndEvent : IBlockEvent;

// Text Block
public sealed record TextStart(int Index = 0, string? Id = null, string? MessageId = null) : IBlockStartEvent;
public sealed record TextDelta(int Index, string Text, string? MessageId = null) : IBlockDeltaEvent;
public sealed record TextEnd(int Index = 0, string? MessageId = null) : IBlockEndEvent; 

// Reasoning Block (LLM)
public sealed record ReasoningStart(int Index = 0, string? Id = null, string? MessageId = null) : IBlockStartEvent;
public sealed record ReasoningDelta(int Index, string Thought, string? MessageId = null) : IBlockDeltaEvent;
public sealed record ReasoningEnd(int Index = 0, string? MessageId = null) : IBlockEndEvent;

// Tool Call Block (LLM)
public sealed record ToolCallStart(int Index, string Id, string Name, string? MessageId = null) : IBlockStartEvent;
public sealed record ToolCallDelta(int Index, string Arguments, string? MessageId = null) : IBlockDeltaEvent;
public sealed record ToolCallEnd(int Index = 0, string? MessageId = null) : IBlockEndEvent;

// Telemetry
public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0) : IMetadata
{
    public int TotalTokens => InputTokens + OutputTokens;
}


