using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Context;
using AgentCore.Memory;
using AgentCore.Tooling;
using System.Collections.Generic;

namespace AgentCore;

/// <summary>
/// Defines the orchestrator runtime loop for coordinating state, LLM execution, and tool execution.
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Executes the orchestration loop on the conversation state.
    /// </summary>
    /// <param name="state">The mutable conversation state (evolved strictly by the runtime loop).</param>
    /// <param name="llm">The pure LLM executor.</param>
    /// <param name="tools">The pure tool executor.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A stream of events generated during execution.</returns>
    IAsyncEnumerable<AgentEvent> RunAsync(
        List<Message> state,
        ILLMExecutor llm,
        IToolExecutor tools,
        CancellationToken ct = default);
}
