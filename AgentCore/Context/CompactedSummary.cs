using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public sealed record CompactedSummary(string Summary) : IContent
{
    public string ForLlm() => 
        $"Context compacted due to overflow. Summary of previous interactions:\n{Summary}";
}
