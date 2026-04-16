namespace AgentCore.Memory;

/// <summary>
/// Outcome types for memory confidence adjustment (AMFS pattern).
/// Success → boost confidence; Failure → reduce confidence; CriticalFailure → mark for pruning.
/// </summary>
public enum OutcomeType
{
    Success,
    MinorFailure,
    Failure,
    CriticalFailure
}
