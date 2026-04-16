using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AgentCore.Tooling;

/// <summary>
/// Default implementation of approval service with in-memory storage
/// </summary>
public class ApprovalService : IApprovalService
{
    private readonly ConcurrentDictionary<string, ToolApprovalRequest> _approvals = new();
    private readonly ILogger<ApprovalService> _logger;
    private readonly int _pollIntervalMs = 500; // Poll every 500ms

    public ApprovalService(ILogger<ApprovalService> logger)
    {
        _logger = logger;
    }

    public ApprovalRegistrationResponse RegisterApprovalRequest(ToolApprovalRequest request)
    {
        var approvalId = Guid.NewGuid().ToString();
        request.ApprovalId = approvalId;
        
        _approvals[approvalId] = request;
        _logger.LogInformation("Registered approval request {ApprovalId} for tool {ToolName}", approvalId, request.ToolName);

        return new ApprovalRegistrationResponse
        {
            ApprovalId = approvalId,
            ExpiresAtMs = request.ExpiresAtMs
        };
    }

    public ApprovalDecisionResponse WaitForDecision(string approvalId, CancellationToken cancellationToken = default)
    {
        if (!_approvals.TryGetValue(approvalId, out var request))
        {
            return new ApprovalDecisionResponse
            {
                ApprovalId = approvalId,
                Status = ApprovalStatus.Rejected,
                Reason = "Approval request not found"
            };
        }

        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(request.ExpiresAtMs - request.CreatedAtMs);

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = DateTime.UtcNow - startTime;
            
            // Check if expired
            if (elapsed >= timeout)
            {
                request.Status = ApprovalStatus.Expired;
                _logger.LogWarning("Approval request {ApprovalId} expired", approvalId);
                return new ApprovalDecisionResponse
                {
                    ApprovalId = approvalId,
                    Status = ApprovalStatus.Expired,
                    Reason = "Approval request timed out"
                };
            }

            // Check if decision made
            if (request.Status != ApprovalStatus.Pending)
            {
                _logger.LogInformation("Approval request {ApprovalId} decided: {Status}", approvalId, request.Status);
                return new ApprovalDecisionResponse
                {
                    ApprovalId = approvalId,
                    Status = request.Status,
                    Reason = request.DecisionReason
                };
            }

            // Wait before next poll
            Task.Delay(_pollIntervalMs, cancellationToken).Wait();
        }

        return new ApprovalDecisionResponse
        {
            ApprovalId = approvalId,
            Status = ApprovalStatus.Rejected,
            Reason = "Operation cancelled"
        };
    }

    public void SubmitDecision(ApprovalDecision decision)
    {
        if (!_approvals.TryGetValue(decision.ApprovalId, out var request))
        {
            _logger.LogWarning("Cannot submit decision for non-existent approval request {ApprovalId}", decision.ApprovalId);
            return;
        }

        if (request.Status != ApprovalStatus.Pending)
        {
            _logger.LogWarning("Approval request {ApprovalId} already decided", decision.ApprovalId);
            return;
        }

        request.Status = decision.Approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        request.DecisionReason = decision.Reason;
        
        _logger.LogInformation("Decision submitted for approval request {ApprovalId}: {Status}", 
            decision.ApprovalId, request.Status);
    }

    public ToolApprovalRequest? GetApprovalRequest(string approvalId)
    {
        return _approvals.TryGetValue(approvalId, out var request) ? request : null;
    }
}
