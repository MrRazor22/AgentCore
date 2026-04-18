using System.ComponentModel;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using Microsoft.Extensions.Logging;

namespace AgentCore.Tooling;

/// <summary>
/// Tools for agent-driven memory operations: reflection and outcome feedback.
/// </summary>
public sealed class MemoryTools(IAgentMemory memory, ILogger<MemoryTools>? logger = null)
{
    private readonly ILogger<MemoryTools> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MemoryTools>.Instance;

    [Description("Reflect deeply on a question using all stored memory and understanding. Creates a persistent observation by synthesizing recalled memories into a coherent answer.")]
    public async Task<string> Reflect(
        [Description("The question to reflect on and synthesize an answer for")] string query,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Agent invoked Reflect: Query={Query}", query);

        // Only works if memory implements ISemanticMemory (MemoryEngine)
        if (memory is not AgentCore.Memory.MemoryEngine engine)
        {
            return "Error: Reflect tool requires MemoryEngine (semantic memory). Current memory implementation does not support reflection.";
        }

        try
        {
            var answer = await engine.ReflectAsync(query, ct).ConfigureAwait(false);
            _logger.LogInformation("Reflect completed successfully for query: {Query}", query);
            return answer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reflect failed.");
            return $"Error during reflection: {ex.Message}";
        }
    }

    [Description("Commit outcome feedback for the current session. Adjusts confidence of recalled memories based on whether they led to success or failure.")]
    public async Task<string> CommitOutcome(
        [Description("The outcome type: Success, MinorFailure, Failure, or CriticalFailure")] string outcome,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Agent invoked CommitOutcome: Outcome={Outcome}", outcome);

        // Only works if memory implements ISemanticMemory (MemoryEngine)
        if (memory is not AgentCore.Memory.MemoryEngine engine)
        {
            return "Error: CommitOutcome tool requires MemoryEngine (semantic memory). Current memory implementation does not support outcome feedback.";
        }

        if (!Enum.TryParse<AgentCore.Memory.OutcomeType>(outcome, ignoreCase: true, out var outcomeType))
        {
            return $"Error: Invalid outcome type '{outcome}'. Valid values: Success, MinorFailure, Failure, CriticalFailure.";
        }

        try
        {
            await engine.CommitOutcomeAsync(outcomeType, ct).ConfigureAwait(false);
            _logger.LogInformation("CommitOutcome completed successfully: {Outcome}", outcomeType);
            return $"Success: Committed {outcomeType} outcome for this session.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommitOutcome failed.");
            return $"Error during outcome commit: {ex.Message}";
        }
    }
}
