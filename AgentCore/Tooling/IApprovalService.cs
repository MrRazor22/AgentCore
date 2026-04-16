namespace AgentCore.Tooling;

/// <summary>
/// Service for handling tool approval requests (OpenClaw-style two-phase approval)
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Phase 1: Register an approval request and return approval ID immediately
    /// </summary>
    ApprovalRegistrationResponse RegisterApprovalRequest(ToolApprovalRequest request);

    /// <summary>
    /// Phase 2: Poll for approval decision
    /// </summary>
    ApprovalDecisionResponse WaitForDecision(string approvalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit a user decision for an approval request
    /// </summary>
    void SubmitDecision(ApprovalDecision decision);

    /// <summary>
    /// Get an approval request by ID
    /// </summary>
    ToolApprovalRequest? GetApprovalRequest(string approvalId);
}
