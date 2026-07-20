using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public interface IToolService
{
    IReadOnlyList<Tool> Tools { get; }
    Task<IReadOnlyList<Message>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default);
}

internal sealed class ToolService : IToolService
{
    private readonly IReadOnlyList<Tool> _toolList;
    private readonly IReadOnlyDictionary<string, Tool> _tools;
    private readonly ILogger<ToolService> _logger;

    public ToolService(
        IReadOnlyList<Tool> tools,
        ILogger<ToolService>? logger = null)
    {
        _toolList = tools ?? Array.Empty<Tool>();
        _tools = _toolList.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger ?? NullLogger<ToolService>.Instance;
    }

    public IReadOnlyList<Tool> Tools => _toolList;

    public async Task<IReadOnlyList<Message>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(calls.Select(c => HandleInternalAsync(c, ct))).ConfigureAwait(false);
        return results.Select(r => new Message(Role.Tool, r)).ToList();
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
            var rawResult = await tool.InvokeAsync(call.ArgumentsObject, ct).ConfigureAwait(false);
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
            _logger.LogWarning("Tool failed: {Name} Error={Message}", call.Name, ex.Message);
            return Failed(call.Id, call.Name, ex.Message);
        }
    }
}
