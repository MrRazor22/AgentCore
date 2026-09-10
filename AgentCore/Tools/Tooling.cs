using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentCore.Tools;

public interface ITooling
{
    IReadOnlyList<ToolDefinition> GetDefinitions();
    IAsyncEnumerable<ToolResult> ExecuteAsync(IReadOnlyList<ToolCall> calls, CancellationToken ct = default);
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

    public async IAsyncEnumerable<ToolResult> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (calls is not { Count: > 0 }) yield break;

        if (!parallel || calls.Count == 1)
        {
            foreach (var call in calls) yield return await ExecuteAsync(call, ct).ConfigureAwait(false);
            yield break;
        }

        using var sem = maxConcurrency is > 0 and int max ? new SemaphoreSlim(max) : null;
        var tasks = calls.Select(async call =>
        {
            if (sem != null) await sem.WaitAsync(ct).ConfigureAwait(false);
            try { return await ExecuteAsync(call, ct).ConfigureAwait(false); }
            finally { sem?.Release(); }
        }).ToList();

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(done);
            yield return await done;
        }
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(call.Name))
            return Fail(call.Id, "Unknown", "Tool name cannot be empty.");

        if (!_tools.TryGetValue(call.Name, out var tool))
            return Fail(call.Id, call.Name, $"Tool '{call.Name}' not registered.");

        var errors = tool.Definition.ParametersSchema.Validate(call.Arguments);
        if (errors.Count > 0)
            return Fail(call.Id, call.Name, string.Join("; ", errors));

        using var cts = timeout > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
        cts?.CancelAfter(timeout!.Value);

        var sw = Stopwatch.StartNew();
        try
        {
            var contents = await tool.InvokeAsync(call.Arguments, cts?.Token ?? ct).ConfigureAwait(false);
            _logger.LogInformation("Tool '{Tool}' executed in {Elapsed}ms", call.Name, sw.ElapsedMilliseconds);
            return new ToolResult(call.Id, contents);
        }
        catch (OperationCanceledException) when (cts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            return Fail(call.Id, call.Name, $"Tool execution timed out after {timeout!.Value.TotalSeconds}s.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var msg = ex.GetBaseException().Message;
            _logger.LogError(ex, "Tool '{Tool}' failed after {Elapsed}ms: {Error}", call.Name, sw.ElapsedMilliseconds, msg);
            return Fail(call.Id, call.Name, msg);
        }
    }

    private ToolResult Fail(string id, string name, string message)
    {
        _logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new(id, [new Text($"Error calling tool '{name}': {message}")]);
    }
}
