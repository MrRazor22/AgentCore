using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;

namespace AgentCore.Tools;

public interface ITooling
{
    IReadOnlyList<Tool> Tools { get; }
    Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default);
}

internal sealed class Tooling : ITooling
{
    private readonly IReadOnlyList<Tool> _toolList;
    private readonly IReadOnlyDictionary<string, Tool> _tools;
    private readonly ILogger<Tooling> _logger;

    public Tooling(
        IReadOnlyList<Tool> tools,
        ILogger<Tooling>? logger = null)
    {
        _toolList = tools ?? Array.Empty<Tool>();
        _tools = _toolList.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger ?? NullLogger<Tooling>.Instance;
    }

    public IReadOnlyList<Tool> Tools => _toolList;

    public async Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
    {
        return await Task.WhenAll(calls.Select(c => HandleInternalAsync(c, ct))).ConfigureAwait(false);
    }

    private static ToolResult Failed(string callId, string toolName, string message)
        => new(callId, new Text($"Error calling tool '{toolName}': {message}"));

    private async Task<ToolResult> HandleInternalAsync(ToolCall call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(call.Name))
        {
            _logger.LogWarning("Tool validation failed: Tool name cannot be empty.");
            return Failed(call.Id, "Unknown", "Tool name cannot be empty.");
        }

        var argsJson = call.Arguments?.ToString() ?? "{}";
        _logger.LogDebug("Executing tool: {Name} Args={ArgsJson}", call.Name, argsJson.Length > 500 ? argsJson[..500] + "..." : argsJson);

        _tools.TryGetValue(call.Name, out var tool);
        if (tool == null)
        {
            _logger.LogWarning("Tool validation failed: Tool '{Name}' not registered.", call.Name);
            return Failed(call.Id, call.Name, $"Tool '{call.Name}' not registered.");
        }

        var errors = tool.ParametersSchema.Validate(call.Arguments);
        if (errors.Any())
        {
            var errorMessage = string.Join("; ", errors);
            _logger.LogWarning("Tool validation failed: {Name} Error={Message}", call.Name, errorMessage);
            return Failed(call.Id, call.Name, errorMessage);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var rawResult = await tool.InvokeAsync(call.Arguments, ct).ConfigureAwait(false);
            IContent result = rawResult switch
            {
                IContent c => c,
                null => new Text(string.Empty),
                string s => new Text(s),
                Exception ex => new Text(ex.Message),
                _ => new Text(JsonSerializer.Serialize(rawResult))
            };

            sw.Stop();
            _logger.LogDebug("Tool completed: {Name} Duration={Ms}ms", call.Name, sw.ElapsedMilliseconds);
            _logger.LogTrace("Tool result: {Name} Result={Content}", call.Name, result?.ForLlm()?.Length > 200 ? result.ForLlm()[..200] + "..." : result?.ForLlm());
            return new ToolResult(call.Id, result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            var actualEx = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                ? tie.InnerException
                : ex;

            _logger.LogError(actualEx, "Tool execution failed: {Name} Error={Message}", call.Name, actualEx.Message);
            return Failed(call.Id, call.Name, actualEx.Message);
        }
    }
}
