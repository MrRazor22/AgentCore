using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AgentCore.Tool;
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonSchema ParametersSchema,
    IReadOnlyList<IMetadata>? Metadata = null);

public interface ITool
{
    ToolDefinition Info { get; }
    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(JsonObject arguments, CancellationToken ct = default);
}
public interface ITooling
{
    IAsyncEnumerable<IMessageEvent> ExecuteAsync(IReadOnlyList<ToolCall> calls, IReadOnlyList<ITool> tools, CancellationToken ct = default);
}

internal sealed class Tooling(
    ILogger<Tooling>? logger = null,
    bool parallel = true,
    int? maxConcurrency = null,
    TimeSpan? timeout = null) : ITooling
{
    private readonly ILogger _logger = logger ?? NullLogger<Tooling>.Instance;

    public async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ITool> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (calls is not { Count: > 0 }) yield break;

        var toolMap = tools?.ToDictionary(t => t.Info.Name, StringComparer.OrdinalIgnoreCase) ?? [];
        var channel = Channel.CreateUnbounded<IMessageEvent>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallel ? (maxConcurrency is > 0 and int max ? max : -1) : 1,
            CancellationToken = ct
        };

        _ = Parallel.ForEachAsync(calls, options, (call, token) => new ValueTask(ExecuteCallAsync(call, toolMap, channel.Writer, token)))
            .ContinueWith(t => channel.Writer.TryComplete(t.Exception?.InnerException));

        await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;
    }

    private async Task ExecuteCallAsync(ToolCall call, Dictionary<string, ITool> toolMap, ChannelWriter<IMessageEvent> writer, CancellationToken ct)
    {
        await writer.WriteAsync(new MessageStart(Role.Tool, Id: call.Id), ct).ConfigureAwait(false);
        await writer.WriteAsync(new MessageDelta(call.Id, Metadata: new ToolCallId(call.Id)), ct).ConfigureAwait(false);

        var (args, parseError) = call.ParseArguments();

        if (string.IsNullOrWhiteSpace(call.Name))
            await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail("Unknown", "Tool name cannot be empty.")), ct).ConfigureAwait(false);
        else if (!toolMap.TryGetValue(call.Name, out var tool))
            await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail(call.Name, $"Tool '{call.Name}' not registered.")), ct).ConfigureAwait(false);
        else if (parseError != null)
            await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail(call.Name, parseError)), ct).ConfigureAwait(false);
        else if (tool.Info.ParametersSchema.Validate(args!) is { Count: > 0 } errors)
            await writer.WriteAsync(new MessageDelta(call.Id, Content: Fail(call.Name, string.Join("; ", errors))), ct).ConfigureAwait(false);
        else
        {
            using var cts = timeout > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
            cts?.CancelAfter(timeout!.Value);

            bool hasResult = false;
            try
            {
                await foreach (var evt in tool.InvokeStreamingAsync(args!, cts?.Token ?? ct).ConfigureAwait(false))
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

        await writer.WriteAsync(new MessageEnd(Id: call.Id), ct).ConfigureAwait(false);
    }

    private Text Fail(string name, string message)
    {
        _logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new Text($"Error calling tool '{name}': {message}");
    }
}
