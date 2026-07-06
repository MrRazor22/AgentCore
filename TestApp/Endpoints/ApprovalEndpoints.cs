using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TestApp.Services;

namespace TestApp.Endpoints;

public static class ApprovalEndpoints
{
    public record ApprovalRequest(bool Approved);

    public static void MapApprovalEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/approvals/{id}", (string id, ApprovalRequest req, IApprovalService approvalService) =>
        {
            var found = approvalService.SetApproval(id, req.Approved);
            return found ? Results.Ok(new { Success = true }) : Results.NotFound("No pending approval found for this ID.");
        });
    }
}
