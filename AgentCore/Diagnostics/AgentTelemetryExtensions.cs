using AgentCore.Chat;
using AgentCore.Diagnostics;
using AgentCore.LLM;
using AgentCore.Tooling;
using System.Diagnostics;

namespace AgentCore;

public static class AgentTelemetryExtensions
{
    public static AgentBuilder WithOpenTelemetry(this AgentBuilder builder)
    {
        builder.UseLLMMiddleware((req, next, ct) =>
        {
            var act = AgentDiagnosticSource.Source.StartActivity("LLM.Invoke");
            act?.SetTag("llm.model", req.Options.Model);
            
            return StreamEventsAsync(req, next, act, ct);
        });

        builder.UseToolMiddleware(async (call, next, ct) =>
        {
            var act = AgentDiagnosticSource.Source.StartActivity("Tool.Invoke");
            act?.SetTag("tool.name", call.Name);

            try
            {
                var res = await next(call, ct);
                if (res.Result is Exception ex)
                {
                    act?.SetStatus(ActivityStatusCode.Error, ex.Message);
                }
                return res;
            }
            finally
            {
                act?.Dispose();
            }
        });

        return builder;
    }

    private static async IAsyncEnumerable<LLMEvent> StreamEventsAsync(
        LLMCall req, 
        Execution.PipelineHandler<LLMCall, IAsyncEnumerable<LLMEvent>> next,
        Activity? act,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var evt in next(req, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            act?.Dispose();
        }
    }
}
