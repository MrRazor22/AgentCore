using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tooling;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore;

/// <summary>
/// A decorator for ILLMExecutor that executes LLM requests through a middleware pipeline.
/// </summary>
public sealed class MiddlewareLLMExecutor : ILLMExecutor
{
    private readonly ILLMExecutor _inner;
    private readonly IReadOnlyList<IMiddleware<LLMCallContext, IAsyncEnumerable<LLMEvent>>> _middlewares;

    public MiddlewareLLMExecutor(
        ILLMExecutor inner,
        IReadOnlyList<IMiddleware<LLMCallContext, IAsyncEnumerable<LLMEvent>>> middlewares)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
    }

    public IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default)
    {
        // Enforce state immutability in middleware by passing IReadOnlyList<Message> in the context
        var context = new LLMCallContext(messages, options, 0);

        var pipeline = new MiddlewarePipeline<LLMCallContext, IAsyncEnumerable<LLMEvent>>((ctx, innerCt) =>
        {
            return Task.FromResult(_inner.StreamAsync(ctx.Messages, ctx.Options, tools, innerCt));
        });

        foreach (var mw in _middlewares)
        {
            pipeline.Use(mw);
        }

        var streamTask = pipeline.InvokeAsyncWithTerminal(context, ct);
        return new AsyncEnumerableWrapper(streamTask);
    }

    private sealed class AsyncEnumerableWrapper : IAsyncEnumerable<LLMEvent>
    {
        private readonly Task<IAsyncEnumerable<LLMEvent>> _streamTask;

        public AsyncEnumerableWrapper(Task<IAsyncEnumerable<LLMEvent>> streamTask)
        {
            _streamTask = streamTask;
        }

        public async IAsyncEnumerator<LLMEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var stream = await _streamTask.ConfigureAwait(false);
            await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }
}

/// <summary>
/// A decorator for IToolExecutor that executes tool calls through a middleware pipeline.
/// </summary>
public sealed class MiddlewareToolExecutor : IToolExecutor
{
    private readonly IToolExecutor _inner;
    private readonly IReadOnlyList<IMiddleware<ToolCall, ToolResult>> _middlewares;

    public MiddlewareToolExecutor(
        IToolExecutor inner,
        IReadOnlyList<IMiddleware<ToolCall, ToolResult>> middlewares)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
    }

    public Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
    {
        var pipeline = new MiddlewarePipeline<ToolCall, ToolResult>(async (c, innerCt) =>
        {
            return await _inner.HandleToolCallAsync(c, innerCt);
        });

        foreach (var mw in _middlewares)
        {
            pipeline.Use(mw);
        }

        return pipeline.InvokeAsyncWithTerminal(call, ct);
    }
}
