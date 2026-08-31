using AgentCore.LLM.Chat;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AgentCore.Tools;

public delegate Task<IReadOnlyList<IContent>?> ToolApprover(ToolCall call, CancellationToken ct);

public sealed class ToolApprovalLayer : ToolingLayer
{
    private readonly ToolApprover _approver;
    private readonly List<Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>> _pendingApprovals = [];
    private readonly object _lock = new();

    public ToolApprovalLayer(ToolApprover approver) => _approver = approver ?? throw new ArgumentNullException(nameof(approver));

    public ToolApprovalLayer(Func<ToolCall, CancellationToken, Task<IContent?>> evaluator)
        : this(async (call, ct) => (await evaluator(call, ct).ConfigureAwait(false)) is { } c ? [c] : null) { }

    public ToolApprovalLayer(Func<ToolCall, CancellationToken, Task<bool>> prompt)
        : this(async (call, ct) => await prompt(call, ct).ConfigureAwait(false) ? null : [new Text($"Execution of tool '{call.Name}' was rejected by the user.")]) { }

    public override Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var task = DecideApprovalAsync(call, ct);
        lock (_lock) _pendingApprovals.Add(task);
        return Task.CompletedTask;
    }

    private async Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)> DecideApprovalAsync(ToolCall call, CancellationToken ct)
    {
        var denial = await _approver(call, ct).ConfigureAwait(false);
        return (call, denial);
    }

    public override async IAsyncEnumerable<ToolResult> StreamResultsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>[] pendingApprovals;
        lock (_lock)
        {
            pendingApprovals = [.. _pendingApprovals];
            _pendingApprovals.Clear();
        }

        var toolResults = Channel.CreateUnbounded<ToolResult>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var checkForResultSignal = Channel.CreateUnbounded<bool>();

        var approvalResults = ProcessApprovalsAsync(pendingApprovals, Inner, checkForResultSignal.Writer, toolResults.Writer, ct);
        var executionResults = StreamExecutionResultsAsync(checkForResultSignal.Reader, toolResults.Writer, ct);

        _ = CompleteAsync(toolResults.Writer, approvalResults, executionResults);

        await foreach (var result in toolResults.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return result;
    }

    private static async Task ProcessApprovalsAsync(
        Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>[] pendingApprovals,
        ITooling inner,
        ChannelWriter<bool> checkForResultsSignal,
        ChannelWriter<ToolResult> toolResults,
        CancellationToken ct)
    {
        try
        {
            var pending = new List<Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>>(pendingApprovals);
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);

                var (call, denial) = await completed.ConfigureAwait(false);

                if (denial is { Count: > 0 })
                {
                    foreach (var content in denial)
                        await toolResults.WriteAsync(new ToolResult(call.Id, content), ct).ConfigureAwait(false);
                }
                else
                {
                    await inner.ExecuteAsync(call, ct).ConfigureAwait(false);
                    checkForResultsSignal.TryWrite(true); //triggers here calls StreamExecutionResultsAsync
                }
            }
        }
        finally
        {
            checkForResultsSignal.TryComplete();
        }
    }

    private async Task StreamExecutionResultsAsync(ChannelReader<bool> checkForResultsSignal, ChannelWriter<ToolResult> toolResults, CancellationToken ct)
    {
        while (await checkForResultsSignal.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (checkForResultsSignal.TryRead(out _)) { } //coalescing notifications
            await foreach (var r in Inner.StreamResultsAsync(ct).ConfigureAwait(false))
                await toolResults.WriteAsync(r, ct).ConfigureAwait(false);
        } 
    }

    private static async Task CompleteAsync(ChannelWriter<ToolResult> toolResults, Task approvalResults, Task executionResults)
    {
        try
        {
            await Task.WhenAll(approvalResults, executionResults).ConfigureAwait(false);
            toolResults.TryComplete();
        }
        catch (Exception ex)
        {
            toolResults.TryComplete(ex);
        }
    }
}
