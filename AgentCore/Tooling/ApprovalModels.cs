using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

/// <summary>
/// Approval decision status
/// </summary>
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired
}

/// <summary>
/// Tool approval request (OpenClaw-style two-phase approval)
/// </summary>
public class ToolApprovalRequest
{
    public required string ApprovalId { get; set; }
    public required string ToolName { get; set; }
    public required string ToolDescription { get; set; }
    public required ToolCategory Category { get; set; }
    public required JsonObject Arguments { get; set; }
    public required long CreatedAtMs { get; set; }
    public required long ExpiresAtMs { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? DecisionReason { get; set; }
}

/// <summary>
/// Phase 1 response: Returns approval ID immediately
/// </summary>
public class ApprovalRegistrationResponse
{
    public required string ApprovalId { get; set; }
    public required long ExpiresAtMs { get; set; }
}

/// <summary>
/// Phase 2 response: Final decision after polling
/// </summary>
public class ApprovalDecisionResponse
{
    public required string ApprovalId { get; set; }
    public required ApprovalStatus Status { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// User decision for approval
/// </summary>
public class ApprovalDecision
{
    public required string ApprovalId { get; set; }
    public required bool Approved { get; set; }
    public string? Reason { get; set; }
}
