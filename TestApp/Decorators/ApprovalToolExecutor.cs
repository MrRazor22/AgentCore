using System.Threading;
using System.Threading.Tasks;
using AgentCore.Conversation;
using AgentCore.Tooling;
using TestApp.Services;

namespace TestApp.Decorators;

public class ApprovalToolExecutor : IToolExecutor
{
    private readonly IToolExecutor _inner;
    private readonly IToolRegistry _registry;
    private readonly IApprovalService _approvalService;
    private readonly string _sessionId;

    public ApprovalToolExecutor(
        IToolExecutor inner,
        IToolRegistry registry,
        IApprovalService approvalService,
        string sessionId)
    {
        _inner = inner;
        _registry = registry;
        _approvalService = approvalService;
        _sessionId = sessionId;
    }

    public async Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
    {
        var tool = _registry.TryGet(call.Name);
        if (tool != null && tool.RequiresApproval)
        {
            bool approved = await _approvalService.RequestApprovalAsync(_sessionId, call).ConfigureAwait(false);
            if (!approved)
            {
                return new ToolResult(call.Id, new Text($"Error calling tool '{call.Name}': Tool execution was denied by the user."));
            }
        }

        return await _inner.HandleToolCallAsync(call, ct).ConfigureAwait(false);
    }
}
