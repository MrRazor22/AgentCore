using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tooling;

namespace AgentCore;

/// <summary>
/// Lifecycle hooks for observing agent execution. Fire-and-forget observation points.
/// No base class, no interface, no registration ceremony. Just assign what you need.
/// </summary>
public sealed class AgentHooks
{
    /// <summary>Called before each LLM call.</summary>
    public Func<LLMCallContext, Task>? OnLLMStart { get; set; }
    
    /// <summary>Called after each LLM call completes.</summary>
    public Func<LLMCallContext, LLMMetaEvent, Task>? OnLLMEnd { get; set; }
    
    /// <summary>Called before each tool execution.</summary>
    public Func<ToolCall, Task>? OnToolStart { get; set; }
    
    /// <summary>Called after each tool execution completes.</summary>
    public Func<ToolCall, ToolResult, Task>? OnToolEnd { get; set; }
    
    /// <summary>Called at the start of agent invocation.</summary>
    public Func<IContent, string, Task>? OnAgentStart { get; set; }
    
    /// <summary>Called at the end of agent invocation.</summary>
    public Func<AgentResponse, Task>? OnAgentEnd { get; set; }
}

/// <summary>Context passed to OnLLMStart and OnLLMEnd hooks.</summary>
public sealed record LLMCallContext(IReadOnlyList<Message> Messages, LLMOptions Options, int StepIndex);
