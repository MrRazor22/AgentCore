using AgentCore.LLM.Chat;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AgentCore.Tools;

public delegate Task<IReadOnlyList<IContent>?> ToolApprovalEvaluator(ToolCall call, CancellationToken ct);

public sealed class ApprovalLayer : ToolingLayer
{
    private readonly ToolApprovalEvaluator _evaluator;
    private readonly List<Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>> _pendingToolCalls = [];
    private readonly object _lock = new();

    public ApprovalLayer(ToolApprovalEvaluator evaluator) => _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

    public ApprovalLayer(Func<ToolCall, CancellationToken, Task<IContent?>> evaluator)
        : this(async (call, ct) => (await evaluator(call, ct).ConfigureAwait(false)) is { } c ? [c] : null) { }

    public ApprovalLayer(Func<ToolCall, CancellationToken, Task<bool>> prompt)
        : this(async (call, ct) => await prompt(call, ct).ConfigureAwait(false) ? null : [new Text($"Execution of tool '{call.Name}' was rejected by the user.")]) { }

    public override Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var task = EvaluateAsync(call, ct);
        lock (_lock) _pendingToolCalls.Add(task);
        return Task.CompletedTask;
    }

    private async Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)> EvaluateAsync(ToolCall call, CancellationToken ct)
    {
        var denial = await _evaluator(call, ct).ConfigureAwait(false);
        return (call, denial);
    }

    public override async IAsyncEnumerable<ToolResult> StreamResultsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>[] pendingToolCalls;
        lock (_lock)
        {
            pendingToolCalls = [.. _pendingToolCalls];
            _pendingToolCalls.Clear();
        }

        var toolResultsStream = Channel.CreateUnbounded<ToolResult>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var checkForResultSignal = Channel.CreateUnbounded<bool>();

        var approvalResults = StreamApprovalsResultsAsync(pendingToolCalls, Inner, checkForResultSignal.Writer, toolResultsStream.Writer, ct);
        var executionResults = StreamExecutionResultsAsync(checkForResultSignal.Reader, toolResultsStream.Writer, ct);

        _ = CompleteAsync(toolResultsStream.Writer, approvalResults, executionResults);

        await foreach (var result in toolResultsStream.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return result;
    }

    private static async Task StreamApprovalsResultsAsync(
        Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>[] pendingToolCalls,
        ITooling inner,
        ChannelWriter<bool> checkForResultSignal,
        ChannelWriter<ToolResult> toolResultsStream,
        CancellationToken ct)
    {
        try
        {
            var pending = new List<Task<(ToolCall Call, IReadOnlyList<IContent>? Denial)>>(pendingToolCalls);
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);

                var (call, denial) = await completed.ConfigureAwait(false);

                if (denial is { Count: > 0 })
                {
                    foreach (var content in denial)
                        await toolResultsStream.WriteAsync(new ToolResult(call.Id, content), ct).ConfigureAwait(false);
                }
                else
                {
                    await inner.ExecuteAsync(call, ct).ConfigureAwait(false);
                    checkForResultSignal.TryWrite(true); //triggers here calls StreamExecutionResultsAsync
                }
            }
        }
        finally
        {
            checkForResultSignal.TryComplete();
        }
    }

    private async Task StreamExecutionResultsAsync(ChannelReader<bool> checkForResultSignal, ChannelWriter<ToolResult> toolResultsStream, CancellationToken ct)
    {
        while (await checkForResultSignal.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (checkForResultSignal.TryRead(out _)) { } //coalescing notifications
            await foreach (var r in Inner.StreamResultsAsync(ct).ConfigureAwait(false))
                await toolResultsStream.WriteAsync(r, ct).ConfigureAwait(false);
        } 
    }

    private static async Task CompleteAsync(ChannelWriter<ToolResult> toolResultsStream, Task approvalResults, Task executionResults)
    {
        try
        {
            await Task.WhenAll(approvalResults, executionResults).ConfigureAwait(false);
            toolResultsStream.TryComplete();
        }
        catch (Exception ex)
        {
            toolResultsStream.TryComplete(ex);
        }
    }
}
