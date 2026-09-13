using AgentCore.LLM.Chat;

namespace AgentCore;
 
public interface IMessageEvent { string? MessageId => null; } 
public sealed record MessageEvent(string MessageId, IMessageEvent Event) : IMessageEvent;
public sealed record MessageStart(Role Role = Role.Assistant, string? MessageId = null) : IMessageEvent;
public sealed record MessageEnd(string? MessageId = null) : IMessageEvent;
public sealed record MetadataEvent(IMetadata Metadata, string? MessageId = null) : IMessageEvent;

 
public interface IContentEvent : IMessageEvent { int Index { get; } }
public interface IContentStart : IContentEvent;
public interface IContentDelta : IContentEvent;
public interface IContentEnd : IContentEvent; 
public sealed record ContentEvent(int Index, IContent Content) : IContentEvent; 
public sealed record TextStart(int Index = 0) : IContentStart;
public sealed record TextDelta(int Index, string Text) : IContentDelta;
public sealed record TextEnd(int Index = 0) : IContentEnd;  
public sealed record ReasoningStart(int Index = 0) : IContentStart;
public sealed record ReasoningDelta(int Index, string Thought) : IContentDelta;
public sealed record ReasoningEnd(int Index = 0) : IContentEnd; 
public sealed record ToolCallStart(int Index, string Id, string Name) : IContentStart;
public sealed record ToolCallDelta(int Index, string Arguments) : IContentDelta;
public sealed record ToolCallEnd(int Index = 0) : IContentEnd; 



