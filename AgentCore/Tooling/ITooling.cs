using AgentCore.Conversation;
using AgentCore.Json;
using AgentCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

public interface ITooling
{
    Task<IReadOnlyList<Message>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default);
}

internal sealed class ToolingService : ITooling
{
    private readonly IToolRegistry _tools;
    private readonly ILogger<ToolingService> _logger;

    public ToolingService(
        IToolRegistry tools,
        ILogger<ToolingService>? logger = null)
    {
        _tools = tools;
        _logger = logger ?? NullLogger<ToolingService>.Instance;
    }

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

        var tool = _tools.TryGet(call.Name);
        if (tool == null)
        {
            _logger.LogWarning("Tool validation failed: Tool '{Name}' not registered.", call.Name);
            return Failed(call.Id, call.Name, $"Tool '{call.Name}' not registered.");
        }

        var errors = tool.ParametersSchema.Validate(call.Arguments);
        if (errors.Any())
        {
            var errorMessage = string.Join("; ", errors.Select(e => e.Message));
            _logger.LogWarning("Tool validation failed: {Name} Error={Message}", call.Name, errorMessage);
            return Failed(call.Id, call.Name, errorMessage);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var rawResult = await tool.InvokeAsync(call.Arguments ?? new JsonObject(), ct).ConfigureAwait(false);
            IContent? result = (rawResult is IContent c) ? c : new Text(rawResult.AsJsonString());

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
