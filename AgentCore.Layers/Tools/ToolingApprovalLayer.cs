using AgentCore.LLM.Chat;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AgentCore.Tools;

public delegate Task<IReadOnlyList<IContent>?> ToolApprovalEvaluator(ToolCall call, CancellationToken ct);

public sealed class ApprovalLayer : ToolingLayer
{
    private readonly ToolApprovalEvaluator _evaluator;
    private readonly List<Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>> _evaluations = [];
    private readonly object _lock = new();

    public ApprovalLayer(ToolApprovalEvaluator evaluator) => _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

    public ApprovalLayer(Func<ToolCall, CancellationToken, Task<IContent?>> evaluator)
        : this(async (call, ct) => (await evaluator(call, ct).ConfigureAwait(false)) is { } c ? [c] : null) { }

    public ApprovalLayer(Func<ToolCall, CancellationToken, Task<bool>> prompt)
        : this(async (call, ct) => await prompt(call, ct).ConfigureAwait(false) ? null : [new Text($"Execution of tool '{call.Name}' was rejected by the user.")]) { }

    public override Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var task = EvaluateAsync(call, ct);
        lock (_lock) _evaluations.Add(task);
        return Task.CompletedTask;
    }

    private async Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)> EvaluateAsync(ToolCall call, CancellationToken ct)
    {
        var denial = await _evaluator(call, ct).ConfigureAwait(false);
        return (call, denial);
    }

    public override async IAsyncEnumerable<ToolResult> StreamResultsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>[] evaluations;
        lock (_lock)
        {
            evaluations = [.. _evaluations];
            _evaluations.Clear();
        }

        var results = Channel.CreateUnbounded<ToolResult>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var dispatchSignal = Channel.CreateUnbounded<bool>();

        var approvals = StreamApprovalsAsync(evaluations, Inner, dispatchSignal.Writer, results.Writer, ct);
        var inner = StreamInnerResultsAsync(dispatchSignal.Reader, results.Writer, ct);

        _ = CompleteAsync(results.Writer, approvals, inner);

        await foreach (var result in results.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return result;
    }

    private static async Task StreamApprovalsAsync(
        Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>[] evaluations,
        ITooling inner,
        ChannelWriter<bool> dispatchSignal,
        ChannelWriter<ToolResult> results,
        CancellationToken ct)
    {
        try
        {
            var pending = new List<Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>>(evaluations);
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);

                var (call, denial) = await completed.ConfigureAwait(false);

                if (denial is { Count: > 0 })
                {
                    foreach (var content in denial)
                        await results.WriteAsync(new ToolResult(call.Id, content), ct).ConfigureAwait(false);
                }
                else
                {
                    await inner.ExecuteAsync(call, ct).ConfigureAwait(false);
                    dispatchSignal.TryWrite(true);
                }
            }
        }
        finally
        {
            dispatchSignal.TryComplete();
        }
    }

    private async Task StreamInnerResultsAsync(ChannelReader<bool> dispatchSignal, ChannelWriter<ToolResult> results, CancellationToken ct)
    {
        while (await dispatchSignal.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (dispatchSignal.TryRead(out _)) { }
            await foreach (var r in Inner.StreamResultsAsync(ct).ConfigureAwait(false))
                await results.WriteAsync(r, ct).ConfigureAwait(false);
        }

        await foreach (var r in Inner.StreamResultsAsync(ct).ConfigureAwait(false))
            await results.WriteAsync(r, ct).ConfigureAwait(false);
    }

    private static async Task CompleteAsync(ChannelWriter<ToolResult> results, Task approvals, Task inner)
    {
        try
        {
            await Task.WhenAll(approvals, inner).ConfigureAwait(false);
            results.TryComplete();
        }
        catch (Exception ex)
        {
            results.TryComplete(ex);
        }
    }
}
