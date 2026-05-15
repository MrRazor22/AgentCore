using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tooling;

namespace AgentCore;

public sealed class AgentHooks
{
    public Func<IContent, string, Task>? OnAgentStart;
    public Func<AgentResponse, Task>? OnAgentEnd;
    public Func<LLMCallContext, Task>? OnLLMStart;
    public Func<LLMCallContext, LLMMetaEvent, Task>? OnLLMEnd;
    public Func<ToolCall, Task>? OnToolStart;
    public Func<ToolCall, ToolResult, Task>? OnToolEnd;

    public Task RaiseAgentStartAsync(IContent input, string sessionId)
    {
        var handler = OnAgentStart;
        return handler != null ? handler.Invoke(input, sessionId) : Task.CompletedTask;
    }

    public Task RaiseAgentEndAsync(AgentResponse response)
    {
        var handler = OnAgentEnd;
        return handler != null ? handler.Invoke(response) : Task.CompletedTask;
    }

    public Task RaiseLLMStartAsync(LLMCallContext context)
    {
        var handler = OnLLMStart;
        return handler != null ? handler.Invoke(context) : Task.CompletedTask;
    }

    public Task RaiseLLMEndAsync(LLMCallContext context, LLMMetaEvent meta)
    {
        var handler = OnLLMEnd;
        return handler != null ? handler.Invoke(context, meta) : Task.CompletedTask;
    }

    public Task RaiseToolStartAsync(ToolCall toolCall)
    {
        var handler = OnToolStart;
        return handler != null ? handler.Invoke(toolCall) : Task.CompletedTask;
    }

    public Task RaiseToolEndAsync(ToolCall toolCall, ToolResult result)
    {
        var handler = OnToolEnd;
        return handler != null ? handler.Invoke(toolCall, result) : Task.CompletedTask;
    }
}

public sealed record LLMCallContext(IReadOnlyList<Message> Messages, LLMOptions Options, int StepIndex);