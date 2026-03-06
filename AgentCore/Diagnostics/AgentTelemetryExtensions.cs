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
        return builder
            .BeforeModelCall((opts, msgs, ct) =>
            {
                var act = AgentDiagnosticSource.Source.StartActivity("LLM.Invoke");
                act?.SetTag("llm.model", msgs.Model);
                return Task.FromResult<IReadOnlyList<LLMEvent>?>(null);
            })
            .AfterModelCall((events, ct) =>
            {
                var act = Activity.Current;
                act?.Dispose();
                return Task.CompletedTask;
            })
            .BeforeToolCall((call, ct) =>
            {
                var act = AgentDiagnosticSource.Source.StartActivity("Tool.Invoke");
                act?.SetTag("tool.name", call.Name);
                return Task.FromResult<IContent?>(null);
            })
            .AfterToolCall((call, res, ct) =>
            {
                var act = Activity.Current;
                if (res is ToolException tex) act?.SetStatus(ActivityStatusCode.Error, tex.Message);
                act?.Dispose();
                return Task.FromResult<IContent?>(null);
            });
    }
}
