using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AgentCore.Execution;

public delegate TResult PipelineHandler<in TRequest, out TResult>(TRequest request, CancellationToken ct);

public delegate TResult PipelineMiddleware<TRequest, TResult>(TRequest request, PipelineHandler<TRequest, TResult> next, CancellationToken ct);

public static class Pipeline<TRequest, TResult>
{
    public static PipelineHandler<TRequest, TResult> Build(
        IEnumerable<PipelineMiddleware<TRequest, TResult>> middlewares,
        PipelineHandler<TRequest, TResult> innerHandler)
    {
        var handler = innerHandler;

        foreach (var middleware in middlewares.Reverse())
        {
            var next = handler;
            handler = (req, ct) => middleware(req, next, ct);
        }

        return handler;
    }
}
