using AgentCore;
using AgentCore.Diagnostics;

namespace AgentCore;

public static class AgentBuilderExtensions
{
    public static AgentBuilder AddDiagnostics(this AgentBuilder builder)
    {
        builder.AddLlmExecutorLayer(inner => new DiagnosticLLMExecutor(inner));
        builder.AddToolExecutorLayer(inner => new DiagnosticToolExecutor(inner));
        builder.AddMemoryLayer(inner => new DiagnosticMemory(inner));
        
        return builder;
    }
}
