using AgentCore.LLM.Chat;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AgentCore.Tools;

/// <summary>
/// Delegate for evaluating tool approval.
/// Returns null/empty if approved (proceed to execute inner tool),
/// or a list of <see cref="IContent"/> items (text, images, media) to emit as tool results if denied.
/// </summary>
public delegate Task<IReadOnlyList<IContent>?> ToolApprovalEvaluator(ToolCall call, CancellationToken ct);

/// <summary>
/// Minimal, pluggable ToolingLayer decorator that intercepts tool calls and delegates approval.
/// </summary>
public sealed class ApprovalLayer : ToolingLayer
{
    private readonly ToolApprovalEvaluator _evaluator;
    private readonly List<Task> _evaluations = [];
    private readonly object _lock = new();

    public ApprovalLayer(ToolApprovalEvaluator evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public ApprovalLayer(Func<ToolCall, CancellationToken, Task<IContent?>> evaluator)
        : this(async (call, ct) =>
        {
            var content = await evaluator(call, ct).ConfigureAwait(false);
            return content is null ? null : [content];
        })
    {
    }

    public ApprovalLayer(Func<ToolCall, CancellationToken, Task<bool>> prompt)
        : this(async (call, ct) => await prompt(call, ct).ConfigureAwait(false) ? null : [new Text("[DENIED] User rejected execution.")])
    {
    }

    public override Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var task = ProcessAsync(call, ct);
        lock (_lock) _evaluations.Add(task);
        return Task.CompletedTask;
    }

    private async Task ProcessAsync(ToolCall call, CancellationToken ct)
    {
        var denial = await _evaluator(call, ct).ConfigureAwait(false);
        if (denial is { Count: > 0 })
            throw new ApprovalDeniedException(call.Id, denial);

        await Inner.ExecuteAsync(call, ct).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ToolResult> StreamResultsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        Task[] evaluations;
        lock (_lock)
        {
            evaluations = [.. _evaluations];
            _evaluations.Clear();
        }

        var channel = Channel.CreateUnbounded<ToolResult>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var innerSignal = Channel.CreateUnbounded<bool>();

        var approvalPump = PumpApprovalsAsync(evaluations, innerSignal.Writer, channel.Writer, ct);
        var innerPump = PumpInnerAsync(evaluations, innerSignal.Reader, channel.Writer, ct);

        _ = CompleteAsync(channel.Writer, approvalPump, innerPump);

        await foreach (var result in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return result;
        }
    }

    private static async Task PumpApprovalsAsync(
        Task[] evaluations,
        ChannelWriter<bool> innerSignal,
        ChannelWriter<ToolResult> writer,
        CancellationToken ct)
    {
        try
        {
            var pending = new List<Task>(evaluations);

            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);

                try
                {
                    await completed.ConfigureAwait(false);
                    // Approved & dispatched to Inner -> signal inner pump immediately!
                    innerSignal.TryWrite(true);
                }
                catch (ApprovalDeniedException ex)
                {
                    foreach (var content in ex.Contents)
                    {
                        await writer.WriteAsync(new ToolResult(ex.CallId, content), ct).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            innerSignal.TryComplete();
        }
    }

    private async Task PumpInnerAsync(
        Task[] evaluations,
        ChannelReader<bool> innerSignal,
        ChannelWriter<ToolResult> writer,
        CancellationToken ct)
    {
        var allEvaluations = Task.WhenAll(evaluations);

        while (await innerSignal.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (innerSignal.TryRead(out _)) { }
            await DrainInnerAsync(writer, ct).ConfigureAwait(false);
        }

        // Final drain once all evaluations have finished
        await DrainInnerAsync(writer, ct).ConfigureAwait(false);

        try
        {
            await allEvaluations.ConfigureAwait(false);
        }
        catch
        {
            // Denials handled in PumpApprovalsAsync; unexpected faults observed by CompleteAsync
        }
    }

    private async Task DrainInnerAsync(ChannelWriter<ToolResult> writer, CancellationToken ct)
    {
        await foreach (var result in Inner.StreamResultsAsync(ct).ConfigureAwait(false))
        {
            await writer.WriteAsync(result, ct).ConfigureAwait(false);
        }
    }

    private static async Task CompleteAsync(ChannelWriter<ToolResult> writer, Task approvalPump, Task innerPump)
    {
        try
        {
            await Task.WhenAll(approvalPump, innerPump).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private sealed class ApprovalDeniedException(string callId, IReadOnlyList<IContent> contents) : Exception
    {
        public string CallId { get; } = callId;
        public IReadOnlyList<IContent> Contents { get; } = contents;
    }
}
