using System.ComponentModel;

namespace AgentCore.Tooling;

/// <summary>
/// Opt-in LLM tool surface for cognitive memory operations.
/// NOT auto-registered — user opts in: .WithTools(new MemoryTools(engine))
/// </summary>
public sealed class MemoryTools(IAgentMemory memory)
{
    [Description("Reflect deeply on a question using all stored memory and understanding. Creates a persistent observation.")]
    public async Task<string> Reflect([Description("The question to reflect on")] string query)
        => await memory.ReflectAsync(query);

    [Description("Report task outcome to tune memory confidence. Use after completing or failing a task.")]
    public async Task<string> CommitOutcome(
        [Description("Outcome: 'success', 'minor_failure', 'failure', or 'critical_failure'")] string outcome)
    {
        if (!Enum.TryParse<OutcomeType>(outcome.Replace("_", ""), ignoreCase: true, out var parsed))
            return $"Unknown outcome '{outcome}'. Use: success, minor_failure, failure, critical_failure.";
        await memory.CommitOutcomeAsync(parsed);
        return $"Outcome '{outcome}' recorded. Memory confidence adjusted.";
    }
}
