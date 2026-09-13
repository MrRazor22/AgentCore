using AgentCore.LLM.Chat;

namespace AgentCore;
 
public interface IMessageEvent { string? Id => null; } 
public sealed record MessageStart(Role Role = Role.Assistant, string? Id = null) : IMessageEvent;
public sealed record MessageDelta(string? Id = null, IContentEvent? Content = null, IMetadata? Metadata = null) : IMessageEvent;
public sealed record MessageEnd(string? Id = null) : IMessageEvent;
 
public interface IContentEvent { int Index => 0; }
public interface IContentStart : IContentEvent;
public interface IContentDelta : IContentEvent;
public interface IContentEnd : IContentEvent; 
public sealed record TextStart(int Index = 0) : IContentStart;
public sealed record TextDelta(int Index, string Text) : IContentDelta;
public sealed record TextEnd(int Index = 0) : IContentEnd;  
public sealed record ReasoningStart(int Index = 0) : IContentStart;
public sealed record ReasoningDelta(int Index, string Thought) : IContentDelta;
public sealed record ReasoningEnd(int Index = 0) : IContentEnd; 
public sealed record ToolCallStart(int Index, string Id, string Name) : IContentStart;
public sealed record ToolCallDelta(int Index, string Arguments) : IContentDelta;
public sealed record ToolCallEnd(int Index = 0) : IContentEnd;



