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
    ValueTask<IReadOnlyList<ToolDefinition>> GetDefinitionsAsync(CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> ExecuteAsync(IReadOnlyList<ToolCall> calls, CancellationToken ct = default);
}

public sealed class Tooling : ITooling
{
    private readonly Dictionary<string, ITool> _tools;
    private readonly IReadOnlyList<ToolDefinition> _definitions;
    private readonly ILogger _logger;
    private readonly bool _parallel;
    private readonly int? _maxConcurrency;
    private readonly TimeSpan? _timeout;

    public IReadOnlyList<ITool> Tools => _tools.Values.ToArray();

    public Tooling(
        IEnumerable<ITool>? tools = null,
        ILogger<Tooling>? logger = null,
        bool parallel = true,
        int? maxConcurrency = null,
        TimeSpan? timeout = null)
    {
        _logger = logger ?? NullLogger<Tooling>.Instance;
        _parallel = parallel;
        _maxConcurrency = maxConcurrency;
        _timeout = timeout;
        _tools = tools?.ToDictionary(t => t.Info.Name, StringComparer.OrdinalIgnoreCase) ?? [];
        _definitions = _tools.Values.Select(t => t.Info).ToArray();
    }

    public Tooling With(
        IEnumerable<ITool>? tools = null,
        ILogger<Tooling>? logger = null,
        bool? parallel = null,
        int? maxConcurrency = null,
        TimeSpan? timeout = null)
        => new(
            tools ?? _tools.Values,
            logger ?? (_logger as ILogger<Tooling>),
            parallel ?? _parallel,
            maxConcurrency ?? _maxConcurrency,
            timeout ?? _timeout);

    public ValueTask<IReadOnlyList<ToolDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
        => new(_definitions);

    public async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (calls is not { Count: > 0 }) yield break;

        var messageId = Guid.NewGuid().ToString("N");
        yield return new MessageStart(Role.Tool, Id: messageId);

        var channel = Channel.CreateUnbounded<IMessageEvent>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _parallel ? (_maxConcurrency is > 0 and int max ? max : -1) : 1,
            CancellationToken = ct
        };

        _ = Parallel.ForEachAsync(calls.Select((call, index) => (call, index)), options, (item, token) => new ValueTask(ExecuteCallAsync(item.call, item.index, messageId, _tools, channel.Writer, token)))
            .ContinueWith(t => channel.Writer.TryComplete(t.Exception?.InnerException));

        await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;

        yield return new MessageEnd(Id: messageId);
    }

    private async Task ExecuteCallAsync(ToolCall call, int index, string messageId, Dictionary<string, ITool> toolMap, ChannelWriter<IMessageEvent> writer, CancellationToken ct)
    {
        var (args, parseError) = call.ParseArguments();
        if (parseError != null || string.IsNullOrWhiteSpace(call.Name) || !toolMap.TryGetValue(call.Name, out var tool))
        {
            var err = parseError ?? (string.IsNullOrWhiteSpace(call.Name) ? "Tool name cannot be empty." : $"Tool '{call.Name}' not registered.");
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResult(call.Id, [Fail(call.Name, err)], isError: true)), ct).ConfigureAwait(false);
            return;
        }

        if (tool.Info.ParametersSchema.Validate(args!) is { Count: > 0 } errors)
        {
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResult(call.Id, [Fail(call.Name, string.Join("; ", errors))], isError: true)), ct).ConfigureAwait(false);
            return;
        }

        using var cts = _timeout > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
        cts?.CancelAfter(_timeout!.Value);

        bool isError = false;
        await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultStart(index, call.Id)), ct).ConfigureAwait(false);
        try
        {
            await foreach (var evt in tool.InvokeStreamingAsync(args!, cts?.Token ?? ct).ConfigureAwait(false))
            {
                if (evt is IToolResultContentEvent e)
                    await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, e)), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            isError = true;
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, Fail(call.Name, $"Tool execution timed out after {_timeout!.Value.TotalSeconds}s."))), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            isError = true;
            _logger.LogError(ex, "Tool '{Tool}' failed: {Error}", call.Name, ex.GetBaseException().Message);
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, Fail(call.Name, ex.GetBaseException().Message))), ct).ConfigureAwait(false);
        }
        await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultEnd(index, IsError: isError)), ct).ConfigureAwait(false);
    }

    private Text Fail(string name, string message)
    {
        _logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new Text($"Error calling tool '{name}': {message}");
    }
}
