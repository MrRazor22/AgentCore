using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        var channel = Channel.CreateUnbounded<IAgentEvent>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallel ? (maxConcurrency is > 0 and int max ? max : -1) : 1,
            CancellationToken = ct
        };

        _ = Parallel.ForEachAsync(calls, options, (call, token) => new ValueTask(ExecuteCallAsync(call, channel.Writer, token)))
            .ContinueWith(t => channel.Writer.TryComplete(t.Exception?.InnerException));

        await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;
    }

    private async Task ExecuteCallAsync(ToolCall call, ChannelWriter<IAgentEvent> writer, CancellationToken ct)
    {
        await writer.WriteAsync(new MessageStart(Role.Tool, MessageId: call.Id, Metadata: [new ToolMetadata(call.Id, call.Name)]), ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(call.Name))
            await writer.WriteAsync(Fail(call.Id, "Unknown", "Tool name cannot be empty."), ct).ConfigureAwait(false);
        else if (!_tools.TryGetValue(call.Name, out var tool))
            await writer.WriteAsync(Fail(call.Id, call.Name, $"Tool '{call.Name}' not registered."), ct).ConfigureAwait(false);
        else if (tool.Definition.ParametersSchema.Validate(call.Arguments) is { Count: > 0 } errors)
            await writer.WriteAsync(Fail(call.Id, call.Name, string.Join("; ", errors)), ct).ConfigureAwait(false);
        else
        {
            using var cts = timeout > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
            cts?.CancelAfter(timeout!.Value);

            bool hasResult = false;
            try
            {
                await foreach (var evt in tool.InvokeStreamingAsync(call.Arguments, cts?.Token ?? ct).ConfigureAwait(false))
                {
                    hasResult = true;
                    await writer.WriteAsync(Tag(evt, call.Id), ct).ConfigureAwait(false);
                }

                if (!hasResult)
                    await writer.WriteAsync(new ContentEvent(0, new Text(string.Empty), MessageId: call.Id), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
            {
                await writer.WriteAsync(Fail(call.Id, call.Name, $"Tool execution timed out after {timeout!.Value.TotalSeconds}s."), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Tool '{Tool}' failed: {Error}", call.Name, ex.GetBaseException().Message);
                await writer.WriteAsync(Fail(call.Id, call.Name, ex.GetBaseException().Message), ct).ConfigureAwait(false);
            }
        }

        await writer.WriteAsync(new MessageEnd(MessageId: call.Id), ct).ConfigureAwait(false);
    }

    private ContentEvent Fail(string id, string name, string message)
    {
        _logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new ContentEvent(0, new Text($"Error calling tool '{name}': {message}"), MessageId: id);
    }

    private static IAgentEvent Tag(IAgentEvent evt, string id) => evt switch
    {
        ContentEvent c => c.MessageId != null ? c : c with { MessageId = id },
        TextDelta td => td.MessageId != null ? td : td with { MessageId = id },
        TextStart ts => ts.MessageId != null ? ts : ts with { MessageId = id },
        TextEnd te => te.MessageId != null ? te : te with { MessageId = id },
        _ => evt
    };
}
