using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using TestApp.Services;

namespace TestApp.Decorators;

public class ObservabilityLlmExecutorDecorator : ILLMExecutor
{
    private readonly ILLMExecutor _inner;
    private readonly IEventPublisher _eventPublisher;
    private readonly string _sessionId;

    public ObservabilityLlmExecutorDecorator(ILLMExecutor inner, IEventPublisher eventPublisher, string sessionId)
    {
        _inner = inner;
        _eventPublisher = eventPublisher;
        _sessionId = sessionId;
    }

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _eventPublisher.Publish(new PipelineStageEvent("Prompt", "Active") { SessionId = _sessionId });
        var lastMsg = messages.LastOrDefault()?.Contents.FirstOrDefault()?.ForLlm() ?? "";
        _eventPublisher.Publish(new PromptBuiltEvent(lastMsg) { SessionId = _sessionId });
        _eventPublisher.Publish(new PipelineStageEvent("Prompt", "Inactive") { SessionId = _sessionId });

        _eventPublisher.Publish(new PipelineStageEvent("LLM", "Active") { SessionId = _sessionId });
        _eventPublisher.Publish(new LLMRequestStartedEvent(options.Model ?? "default") { SessionId = _sessionId });

        var sw = Stopwatch.StartNew();

        await foreach (var evt in _inner.StreamAsync(messages, options, ct).ConfigureAwait(false))
        {
            if (evt is LLMMetaEvent meta)
            {
                sw.Stop();
                _eventPublisher.Publish(new LLMResponseReceivedEvent(meta.Usage.InputTokens, meta.Usage.OutputTokens, sw.Elapsed.TotalMilliseconds) { SessionId = _sessionId });
                _eventPublisher.Publish(new PipelineStageEvent("LLM", "Inactive") { SessionId = _sessionId });
            }
            yield return evt;
        }
    }
}

public class ObservabilityToolExecutorDecorator : IToolExecutor
{
    private readonly IToolExecutor _inner;
    private readonly IEventPublisher _eventPublisher;
    private readonly string _sessionId;

    public ObservabilityToolExecutorDecorator(IToolExecutor inner, IEventPublisher eventPublisher, string sessionId)
    {
        _inner = inner;
        _eventPublisher = eventPublisher;
        _sessionId = sessionId;
    }

    public async Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
    {
        _eventPublisher.Publish(new PipelineStageEvent("Tool", "Active") { SessionId = _sessionId });
        _eventPublisher.Publish(new ToolInvokingEvent(call.Name, call.Arguments.ToString() ?? "{}") { SessionId = _sessionId });

        try
        {
            var result = await _inner.HandleToolCallAsync(call, ct).ConfigureAwait(false);
            
            string resultStr = result.Result?.ForLlm() ?? "";
            if (resultStr.Length > 200) resultStr = resultStr[..200] + "...";
            
            _eventPublisher.Publish(new ToolCompletedEvent(call.Name, resultStr) { SessionId = _sessionId });
            return result;
        }
        finally
        {
            _eventPublisher.Publish(new PipelineStageEvent("Tool", "Inactive") { SessionId = _sessionId });
        }
    }
}

public class ObservabilityMemoryDecorator : IMemory
{
    private readonly IMemory _inner;
    private readonly IEventPublisher _eventPublisher;
    private readonly string _sessionId;

    public ObservabilityMemoryDecorator(IMemory inner, IEventPublisher eventPublisher, string sessionId)
    {
        _inner = inner;
        _eventPublisher = eventPublisher;
        _sessionId = sessionId;
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        _eventPublisher.Publish(new PipelineStageEvent("Memory", "Active") { SessionId = _sessionId });
        await _inner.RememberAsync(completedTurn, ct).ConfigureAwait(false);
        
        var allHistory = await _inner.RecallAsync(new Message(Role.User, new Text("")), new TokenBudget(0), ct).ConfigureAwait(false);
        _eventPublisher.Publish(new MemoryUpdatedEvent(allHistory.Count) { SessionId = _sessionId });
        _eventPublisher.Publish(new PipelineStageEvent("Memory", "Inactive") { SessionId = _sessionId });
    }

    public async Task<IReadOnlyList<Message>> RecallAsync(Message currentInput, TokenBudget budget, CancellationToken ct = default)
    {
        _eventPublisher.Publish(new PipelineStageEvent("Memory", "Active") { SessionId = _sessionId });
        var result = await _inner.RecallAsync(currentInput, budget, ct).ConfigureAwait(false);
        _eventPublisher.Publish(new MemoryUpdatedEvent(result.Count) { SessionId = _sessionId });
        _eventPublisher.Publish(new PipelineStageEvent("Memory", "Inactive") { SessionId = _sessionId });
        return result;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _inner.ClearAsync(ct).ConfigureAwait(false);
        _eventPublisher.Publish(new MemoryUpdatedEvent(0) { SessionId = _sessionId });
    }
}
