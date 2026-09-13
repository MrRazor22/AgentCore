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
    IAsyncEnumerable<IMessageEvent> ExecuteAsync(IReadOnlyList<ToolCall> calls, CancellationToken ct = default);
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

    public async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (calls is not { Count: > 0 }) yield break;

        var channel = Channel.CreateUnbounded<IMessageEvent>();
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

    private async Task ExecuteCallAsync(ToolCall call, ChannelWriter<IMessageEvent> writer, CancellationToken ct)
    {
        await writer.WriteAsync(new MessageStart(Role.Tool, MessageId: call.Id), ct).ConfigureAwait(false);
        await writer.WriteAsync(new MessageDelta(call.Id, Metadata: new ToolCallId(call.Id)), ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(call.Name))
            await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail("Unknown", "Tool name cannot be empty.")), ct).ConfigureAwait(false);
        else if (!_tools.TryGetValue(call.Name, out var tool))
            await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail(call.Name, $"Tool '{call.Name}' not registered.")), ct).ConfigureAwait(false);
        else if (tool.Definition.ParametersSchema.Validate(call.Arguments) is { Count: > 0 } errors)
            await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail(call.Name, string.Join("; ", errors))), ct).ConfigureAwait(false);
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
                    await writer.WriteAsync(new MessageDelta(call.Id, Content: evt), ct).ConfigureAwait(false);
                }

                if (!hasResult)
                    await writer.WriteAsync(new MessageDelta(call.Id, Content: new Text(string.Empty)), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
            {
                await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail(call.Name, $"Tool execution timed out after {timeout!.Value.TotalSeconds}s.")), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Tool '{Tool}' failed: {Error}", call.Name, ex.GetBaseException().Message);
                await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail(call.Name, ex.GetBaseException().Message)), ct).ConfigureAwait(false);
            }
        }

        await writer.WriteAsync(new MessageEnd(MessageId: call.Id), ct).ConfigureAwait(false);
    }

    private Text Fail(string name, string message)
    {
        _logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new Text($"Error calling tool '{name}': {message}");
    }
}
