using System.Text.Json.Serialization;
using AgentCore.LLM.Chat;

namespace AgentCore;
 
[JsonPolymorphic]
[JsonDerivedType(typeof(MessageStart), "start")]
[JsonDerivedType(typeof(MessageDelta), "delta")]
[JsonDerivedType(typeof(MessageEnd), "end")]
[JsonDerivedType(typeof(Message), "message")]
public interface IMessageEvent { string? Id => null; } 
public sealed record MessageStart(Role Role = Role.Assistant, string? Id = null) : IMessageEvent;
public sealed record MessageDelta(string? Id = null, IContentEvent? Content = null, IMetadata? Metadata = null) : IMessageEvent;
public sealed record MessageEnd(string? Id = null) : IMessageEvent;
 
[JsonPolymorphic]
[JsonDerivedType(typeof(TextStart), "text_start")]
[JsonDerivedType(typeof(TextDelta), "text_delta")]
[JsonDerivedType(typeof(TextEnd), "text_end")]
[JsonDerivedType(typeof(ReasoningStart), "reasoning_start")]
[JsonDerivedType(typeof(ReasoningDelta), "reasoning_delta")]
[JsonDerivedType(typeof(ReasoningEnd), "reasoning_end")]
[JsonDerivedType(typeof(ToolCallStart), "tool_start")]
[JsonDerivedType(typeof(ToolCallDelta), "tool_delta")]
[JsonDerivedType(typeof(ToolCallEnd), "tool_end")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(Reasoning), "reasoning")]
[JsonDerivedType(typeof(ToolCall), "tool")]
[JsonDerivedType(typeof(Image), "image")]
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



