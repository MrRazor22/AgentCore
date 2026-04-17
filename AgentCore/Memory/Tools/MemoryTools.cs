using System.ComponentModel;

namespace AgentCore.Memory.Tools;

/// <summary>
/// Opt-in LLM tool surface for semantic memory operations (AMFS).
/// Requires ISemanticMemory implementation (MemoryEngine). NOT auto-registered.
/// </summary>
public sealed class MemoryTools(ISemanticMemory memory)
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
