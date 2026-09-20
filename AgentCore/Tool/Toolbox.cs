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
    ToolDefinition Definition { get; }
    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(JsonObject arguments, CancellationToken ct = default);
}
public interface IToolbox
{
    ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> ExecuteAsync(IReadOnlyList<ToolCall> calls, CancellationToken ct = default);
}

public sealed class Toolbox : IToolbox
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ITool> _tools;

    public IReadOnlyList<ITool> Tools => _tools.Values.ToArray();
    public ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default) => new(_tools.Values.ToArray());
    public ILogger Logger { get; }
    public bool ParallelExecution { get; }
    public int? MaxConcurrency { get; }
    public TimeSpan? Timeout { get; }

    public Toolbox(
        IEnumerable<ITool>? tools = null,
        ILogger<Toolbox>? logger = null,
        bool parallel = true,
        int? maxConcurrency = null,
        TimeSpan? timeout = null)
    {
        Logger = logger ?? NullLogger<Toolbox>.Instance;
        ParallelExecution = parallel;
        MaxConcurrency = maxConcurrency;
        Timeout = timeout;
        _tools = new System.Collections.Concurrent.ConcurrentDictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        if (tools != null)
        {
            foreach (var t in tools)
                _tools[t.Definition.Name] = t;
        }
    }

    public void Add(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var t in tools)
            _tools[t.Definition.Name] = t;
    }

    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryRemove(name, out _);
    }

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
            MaxDegreeOfParallelism = ParallelExecution ? (MaxConcurrency is > 0 and int max ? max : -1) : 1,
            CancellationToken = ct
        };

        _ = Parallel.ForEachAsync(calls.Select((call, index) => (call, index)), options, (item, token) => new ValueTask(ExecuteCallAsync(item.call, item.index, messageId, channel.Writer, token)))
            .ContinueWith(t => channel.Writer.TryComplete(t.Exception?.InnerException));

        await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;

        yield return new MessageEnd(Id: messageId);
    }

    private async Task ExecuteCallAsync(ToolCall call, int index, string messageId, ChannelWriter<IMessageEvent> writer, CancellationToken ct)
    {
        var (args, parseError) = call.ParseArguments();
        if (parseError != null || string.IsNullOrWhiteSpace(call.Name) || !_tools.TryGetValue(call.Name, out var tool))
        {
            var err = parseError ?? (string.IsNullOrWhiteSpace(call.Name) ? "Tool name cannot be empty." : $"Tool '{call.Name}' not registered.");
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResult(call.Id, [Fail(call.Name, err)], isError: true)), ct).ConfigureAwait(false);
            return;
        }

        if (tool.Definition.ParametersSchema.Validate(args!) is { Count: > 0 } errors)
        {
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResult(call.Id, [Fail(call.Name, string.Join("; ", errors))], isError: true)), ct).ConfigureAwait(false);
            return;
        }

        using var cts = Timeout > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
        cts?.CancelAfter(Timeout!.Value);

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
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, Fail(call.Name, $"Tool execution timed out after {Timeout!.Value.TotalSeconds}s."))), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            isError = true;
            Logger.LogError(ex, "Tool '{Tool}' failed: {Error}", call.Name, ex.GetBaseException().Message);
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, Fail(call.Name, ex.GetBaseException().Message))), ct).ConfigureAwait(false);
        }
        await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultEnd(index, IsError: isError)), ct).ConfigureAwait(false);
    }

    private Text Fail(string name, string message)
    {
        Logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new Text($"Error calling tool '{name}': {message}");
    }
}
