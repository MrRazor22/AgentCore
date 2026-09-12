using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AgentCore.Tools;

public interface ITooling
{
    IReadOnlyList<ToolDefinition> GetDefinitions();
    IAsyncEnumerable<IAgentEvent> ExecuteStreamingAsync(IReadOnlyList<ToolCall> calls, CancellationToken ct = default);
}

internal sealed class Tooling(
    IEnumerable<ITool>? tools,
    ILogger<Tooling>? logger = null,
    bool parallel = true,
    int? maxConcurrency = null,
    TimeSpan? timeout = null) : ITooling
{
    private readonly Dictionary<string, ITool> _tools = tools?.ToDictionary(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase) ?? [];
    private readonly ToolDefinition[] _definitions = tools?.Select(t => t.Definition).ToArray() ?? [];
    private readonly ILogger _logger = logger ?? NullLogger<Tooling>.Instance;

    public IReadOnlyList<ToolDefinition> GetDefinitions() => _definitions;

    public async IAsyncEnumerable<IAgentEvent> ExecuteStreamingAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (calls is not { Count: > 0 }) yield break;

        if (!parallel || calls.Count == 1)
        {
            foreach (var call in calls)
            {
                await foreach (var evt in ExecuteCallStreamingAsync(call, ct).ConfigureAwait(false))
                {
                    yield return evt;
                }
            }
            yield break;
        }

        var channel = Channel.CreateUnbounded<IAgentEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var sem = maxConcurrency is > 0 and int max ? new SemaphoreSlim(max) : null;

        var tasks = calls.Select(async call =>
        {
            if (sem != null) await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await foreach (var evt in ExecuteCallStreamingAsync(call, ct).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(evt, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                sem?.Release();
            }
        }).ToList();

        _ = Task.WhenAll(tasks).ContinueWith(t =>
        {
            if (t.IsFaulted) channel.Writer.TryComplete(t.Exception!.InnerException);
            else channel.Writer.TryComplete();
        }, TaskScheduler.Default);

        while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (channel.Reader.TryRead(out var evt))
            {
                yield return evt;
            }
        }
    }

    private async IAsyncEnumerable<IAgentEvent> ExecuteCallStreamingAsync(
        ToolCall call,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new MessageStart(Role.Tool, MessageId: call.Id, Metadata: [new ToolMetadata(call.Id, call.Name)]);

        await foreach (var evt in ExecuteCallInternalAsync(call, ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        yield return new MessageEnd(MessageId: call.Id);
    }

    private async IAsyncEnumerable<IAgentEvent> ExecuteCallInternalAsync(
        ToolCall call,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(call.Name))
        {
            yield return Fail(call.Id, "Unknown", "Tool name cannot be empty.");
            yield break;
        }

        if (!_tools.TryGetValue(call.Name, out var tool))
        {
            yield return Fail(call.Id, call.Name, $"Tool '{call.Name}' not registered.");
            yield break;
        }

        var errors = tool.Definition.ParametersSchema.Validate(call.Arguments);
        if (errors.Count > 0)
        {
            yield return Fail(call.Id, call.Name, string.Join("; ", errors));
            yield break;
        }

        using var cts = timeout > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
        cts?.CancelAfter(timeout!.Value);

        var sw = Stopwatch.StartNew();
        IAsyncEnumerator<IBlockEvent>? enumerator = null;
        string? initError = null;
        try
        {
            enumerator = tool.InvokeStreamingAsync(call.Arguments, cts?.Token ?? ct).GetAsyncEnumerator(cts?.Token ?? ct);
        }
        catch (Exception ex)
        {
            initError = ex.GetBaseException().Message;
            _logger.LogError(ex, "Tool '{Tool}' failed: {Error}", call.Name, initError);
        }

        if (initError != null)
        {
            yield return Fail(call.Id, call.Name, initError);
            yield break;
        }

        bool hasResult = false;
        string? loopError = null;
        try
        {
            while (true)
            {
                IBlockEvent? evt = null;
                try
                {
                    if (!await enumerator!.MoveNextAsync().ConfigureAwait(false))
                        break;
                    evt = enumerator.Current;
                }
                catch (OperationCanceledException) when (cts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
                {
                    loopError = $"Tool execution timed out after {timeout!.Value.TotalSeconds}s.";
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    loopError = ex.GetBaseException().Message;
                    _logger.LogError(ex, "Tool '{Tool}' failed after {Elapsed}ms: {Error}", call.Name, sw.ElapsedMilliseconds, loopError);
                    break;
                }

                if (evt != null)
                {
                    hasResult = true;
                    yield return WithCallId(evt, call.Id);
                }
            }
        }
        finally
        {
            if (enumerator != null)
                await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (loopError != null)
        {
            yield return Fail(call.Id, call.Name, loopError);
            yield break;
        }

        _logger.LogInformation("Tool '{Tool}' executed in {Elapsed}ms", call.Name, sw.ElapsedMilliseconds);

        if (!hasResult)
        {
            yield return new ContentBlock(0, new Text(string.Empty), MessageId: call.Id);
        }
    }

    private ContentBlock Fail(string id, string name, string message)
    {
        _logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new ContentBlock(0, new Text($"Error calling tool '{name}': {message}"), MessageId: id);
    }

    private static IBlockEvent WithCallId(IBlockEvent evt, string callId) => evt switch
    {
        TextStart s => s.MessageId == null ? s with { MessageId = callId } : s,
        TextDelta d => d.MessageId == null ? d with { MessageId = callId } : d,
        TextEnd e => e.MessageId == null ? e with { MessageId = callId } : e,
        ContentBlock cb => cb.MessageId == null ? cb with { MessageId = callId } : cb,
        _ => evt
    };
}
