using AgentCore.LLM;
using AgentCore.LLM.Chat;
using System.Runtime.CompilerServices;

namespace AgentCore.Tool;

public delegate Task<IReadOnlyList<IContent>?> ToolApprover(ToolCall call, CancellationToken ct);

public sealed class ToolApprovalLayer : ToolingLayer
{
    private readonly ToolApprover _approver;

    public ToolApprovalLayer(ITooling inner, ToolApprover approver) : base(inner) => _approver = approver ?? throw new ArgumentNullException(nameof(approver));

    public ToolApprovalLayer(ITooling inner, Func<ToolCall, CancellationToken, Task<IContent?>> evaluator)
        : this(inner, async (call, ct) => (await evaluator(call, ct).ConfigureAwait(false)) is { } c ? [c] : null) { }

    public ToolApprovalLayer(ITooling inner, Func<ToolCall, CancellationToken, Task<bool>> prompt)
        : this(inner, async (call, ct) => await prompt(call, ct).ConfigureAwait(false) ? null : [new Text($"Execution of tool '{call.Name}' was rejected by the user.")]) { }


    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ITool> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var allowedCalls = new List<ToolCall>();

        foreach (var call in calls)
        {
            var denial = await _approver(call, ct).ConfigureAwait(false);
            if (denial is { Count: > 0 })
            {
                yield return new MessageStart(Role.Tool, Id: call.Id);
                yield return new MessageDelta(call.Id, Metadata: new ToolCallId(call.Id));
                for (int i = 0; i < denial.Count; i++)
                {
                    var item = denial[i];
                    var content = item is IContent c ? c : new Text(item.ToString() ?? string.Empty);
                    yield return new MessageDelta(call.Id, Content: content);
                }
                yield return new MessageEnd(Id: call.Id);
            }
            else
            {
                allowedCalls.Add(call);
            }
        }

        if (allowedCalls.Count > 0)
        {
            await foreach (var evt in base.ExecuteAsync(allowedCalls, tools, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }
}
