using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace AgentCore.Diagnostics;

public class DiagnosticLLMExecutor : ILLMExecutor
{
    private readonly ILLMExecutor _inner;

    public DiagnosticLLMExecutor(ILLMExecutor inner)
    {
        _inner = inner;
    }

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages, 
        LLMOptions options, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var span = Tracer.ActiveTrace?.StartSpan("LLM Generate", AgentSpanKind.Llm);
        span?.SetAttribute("llm.model", options.Model);
        
        // Count input messages loosely for visualization
        span?.SetAttribute("llm.input_messages", messages.Count);

        IAsyncEnumerator<LLMEvent> enumerator;
        try
        {
            enumerator = _inner.StreamAsync(messages, options, ct).GetAsyncEnumerator(ct);
        }
        catch (Exception ex)
        {
            span?.Fail(ex);
            throw;
        }

        bool hasMore;
        do
        {
            try
            {
                hasMore = await enumerator.MoveNextAsync();
            }
            catch (Exception ex)
            {
                span?.Fail(ex);
                throw;
            }

            if (hasMore)
            {
                var @event = enumerator.Current;
                
                if (@event is LLMMetaEvent meta)
                {
                    span?.SetAttribute("llm.usage.input_tokens", meta.Usage.InputTokens);
                    span?.SetAttribute("llm.usage.output_tokens", meta.Usage.OutputTokens);
                    span?.SetAttribute("llm.usage.reasoning_tokens", meta.Usage.ReasoningTokens);
                    span?.SetAttribute("llm.usage.total_tokens", meta.Usage.Total);
                    span?.SetAttribute("llm.finish_reason", meta.FinishReason.ToString());
                }
                
                yield return @event;
            }
        }
        while (hasMore);
        
        span?.SetStatus(SpanStatus.Success);
    }
}

public class DiagnosticToolExecutor : IToolExecutor
{
    private readonly IToolExecutor _inner;

    public DiagnosticToolExecutor(IToolExecutor inner)
    {
        _inner = inner;
    }

    public async Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
    {
        using var span = Tracer.ActiveTrace?.StartSpan($"Tool: {call.Name}", AgentSpanKind.Tool, call.Arguments?.ToString());
        span?.SetAttribute("tool.name", call.Name);
        span?.SetAttribute("tool.id", call.Id);

        try
        {
            var result = await _inner.HandleToolCallAsync(call, ct);
            span?.SetOutput(result.Result?.ForLlm() ?? string.Empty);
            span?.SetStatus(SpanStatus.Success);
            return result;
        }
        catch (Exception ex)
        {
            span?.Fail(ex);
            throw;
        }
    }
}

public class DiagnosticMemory : IMemory
{
    private readonly IMemory _inner;

    public DiagnosticMemory(IMemory inner)
    {
        _inner = inner;
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        using var span = Tracer.ActiveTrace?.StartSpan("Memory Remember", AgentSpanKind.Memory);
        span?.SetAttribute("memory.messages_count", completedTurn.Count);
        
        try
        {
            await _inner.RememberAsync(completedTurn, ct);
            span?.SetStatus(SpanStatus.Success);
        }
        catch (Exception ex)
        {
            span?.Fail(ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<Message>> RecallAsync(Message currentInput, TokenBudget budget, CancellationToken ct = default)
    {
        using var span = Tracer.ActiveTrace?.StartSpan("Memory Recall", AgentSpanKind.Memory);
        span?.SetAttribute("memory.budget", budget.Tokens);
        
        try
        {
            var result = await _inner.RecallAsync(currentInput, budget, ct);
            span?.SetAttribute("memory.recalled_count", result.Count);
            span?.SetStatus(SpanStatus.Success);
            return result;
        }
        catch (Exception ex)
        {
            span?.Fail(ex);
            throw;
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        using var span = Tracer.ActiveTrace?.StartSpan("Memory Clear", AgentSpanKind.Memory);
        try
        {
            await _inner.ClearAsync(ct);
            span?.SetStatus(SpanStatus.Success);
        }
        catch (Exception ex)
        {
            span?.Fail(ex);
            throw;
        }
    }
}

public class DiagnosticAgent : IAgent
{
    private readonly IAgent _inner;
    private readonly IAgentTracer _tracer;
    private readonly string _sessionId;

    public DiagnosticAgent(IAgent inner, IAgentTracer tracer, string sessionId)
    {
        _inner = inner;
        _tracer = tracer;
        _sessionId = sessionId;
    }

    public async Task<IContent> InvokeAsync(IContent input, CancellationToken ct = default)
    {
        using var trace = _tracer.StartTrace("Agent Invoke", _sessionId);
        using var span = trace.StartSpan("Invoke", AgentSpanKind.Agent, input.ForLlm());
        try
        {
            var result = await _inner.InvokeAsync(input, ct);
            span.SetOutput(result.ForLlm());
            span.SetStatus(SpanStatus.Success);
            trace.SetStatus(SpanStatus.Success);
            return result;
        }
        catch (Exception ex)
        {
            span.Fail(ex);
            trace.SetStatus(SpanStatus.Error, ex.Message);
            throw;
        }
    }

    public async Task<T> InvokeAsync<T>(IContent input, CancellationToken ct = default)
    {
        using var trace = _tracer.StartTrace("Agent Invoke<T>", _sessionId);
        using var span = trace.StartSpan("Invoke<T>", AgentSpanKind.Agent, input.ForLlm());
        try
        {
            var result = await _inner.InvokeAsync<T>(input, ct);
            span.SetOutput(result?.ToString() ?? string.Empty);
            span.SetStatus(SpanStatus.Success);
            trace.SetStatus(SpanStatus.Success);
            return result!;
        }
        catch (Exception ex)
        {
            span.Fail(ex);
            trace.SetStatus(SpanStatus.Error, ex.Message);
            throw;
        }
    }

    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var trace = _tracer.StartTrace("Agent Stream", _sessionId);
        using var span = trace.StartSpan("InvokeStreaming", AgentSpanKind.Agent, input.ForLlm());
        
        IAsyncEnumerator<AgentEvent> enumerator;
        try
        {
            enumerator = _inner.InvokeStreamingAsync(input, ct).GetAsyncEnumerator(ct);
        }
        catch (Exception ex)
        {
            span.Fail(ex);
            trace.SetStatus(SpanStatus.Error, ex.Message);
            throw;
        }

        bool hasMore;
        do
        {
            try
            {
                hasMore = await enumerator.MoveNextAsync();
            }
            catch (Exception ex)
            {
                span.Fail(ex);
                trace.SetStatus(SpanStatus.Error, ex.Message);
                throw;
            }

            if (hasMore)
            {
                yield return enumerator.Current;
            }
        }
        while (hasMore);
        
        span.SetStatus(SpanStatus.Success);
        trace.SetStatus(SpanStatus.Success);
    }
}
