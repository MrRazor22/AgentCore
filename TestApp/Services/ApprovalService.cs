using System.Collections.Concurrent;
using System.Threading.Tasks;
using AgentCore.Conversation;

namespace TestApp.Services;

public interface IApprovalService
{
    Task<bool> RequestApprovalAsync(string sessionId, ToolCall call);
    bool SetApproval(string callId, bool approved);
}

public class ApprovalService : IApprovalService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingApprovals = new();
    private readonly IEventPublisher _eventPublisher;

    public ApprovalService(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public async Task<bool> RequestApprovalAsync(string sessionId, ToolCall call)
    {
        var tcs = new TaskCompletionSource<bool>();
        _pendingApprovals[call.Id] = tcs;

        _eventPublisher.Publish(new ApprovalRequestedEvent(call.Id, call.Name, call.Arguments.ToString() ?? "{}")
        {
            SessionId = sessionId
        });

        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pendingApprovals.TryRemove(call.Id, out _);
        }
    }

    public bool SetApproval(string callId, bool approved)
    {
        if (_pendingApprovals.TryGetValue(callId, out var tcs))
        {
            tcs.TrySetResult(approved);
            return true;
        }
        return false;
    }
}
