using AgentCore.LLM.Chat;

namespace AgentCore.Tools;

public delegate Task<IReadOnlyList<IContent>?> ToolApprover(ToolCall call, CancellationToken ct);

public sealed class ToolApprovalLayer : ToolingLayer
{
    private readonly ToolApprover _approver;

    public ToolApprovalLayer(ToolApprover approver) => _approver = approver ?? throw new ArgumentNullException(nameof(approver));

    public ToolApprovalLayer(Func<ToolCall, CancellationToken, Task<IContent?>> evaluator)
        : this(async (call, ct) => (await evaluator(call, ct).ConfigureAwait(false)) is { } c ? [c] : null) { }

    public ToolApprovalLayer(Func<ToolCall, CancellationToken, Task<bool>> prompt)
        : this(async (call, ct) => await prompt(call, ct).ConfigureAwait(false) ? null : [new Text($"Execution of tool '{call.Name}' was rejected by the user.")]) { }

    public override async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var denial = await _approver(call, ct).ConfigureAwait(false);

        if (denial is { Count: > 0 })
            return new ToolResult(call.Id, denial);

        return await Inner.ExecuteAsync(call, ct).ConfigureAwait(false);
    }
}
