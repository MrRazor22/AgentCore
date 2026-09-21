using System.Runtime.CompilerServices;
using AgentCore;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCoreT03;

public delegate Task<bool> ApprovalDelegate(ToolCall call, CancellationToken ct = default);

public sealed class ApprovalLayer(
    ApprovalDelegate approver,
    Func<ToolCall, bool>? isSensitive = null,
    IToolbox? inner = null) : ToolboxLayer(inner)
{
    private readonly ApprovalDelegate _approver = approver ?? throw new ArgumentNullException(nameof(approver));
    private readonly Func<ToolCall, bool> _isSensitive = isSensitive ?? (_ => true);

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var allowedCalls = new List<ToolCall>();

        foreach (var call in calls)
        {
            if (_isSensitive(call))
            {
                var approved = await _approver(call, ct).ConfigureAwait(false);
                if (!approved)
                {
                    yield return new MessageStart(Role.Tool, Id: call.Id);
                    yield return new MessageDelta(call.Id, Content: new ToolResult(
                        call.Id,
                        [new Text($"Execution of tool '{call.Name}' was rejected by human-in-the-loop reviewer.")],
                        isError: true));
                    yield return new MessageEnd(Id: call.Id);
                    continue;
                }
            }

            allowedCalls.Add(call);
        }

        if (allowedCalls.Count > 0)
        {
            await foreach (var evt in base.ExecuteAsync(allowedCalls, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }
}
