using AgentCore.Conversation;
using AgentCore.Json;
using AgentCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

public interface IToolExecutor
{
    Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default);
}

internal sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry _tools;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(
        IToolRegistry tools,
        ILogger<ToolExecutor> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    public Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
        => HandleInternalAsync(call, ct);

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

        var method = tool.Method!;
        if (!TryParseToolParams(method, call.Arguments, out var values, out var errorMessage))
        {
            _logger.LogWarning("Tool validation failed: {Name} Error={Message}", call.Name, errorMessage);
            return Failed(call.Id, call.Name, errorMessage!);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var finalArgs = InjectCancellationToken(values!, method, ct);
            var rawResult = await tool.Invoker!(finalArgs).ConfigureAwait(false);
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

    private static bool TryParseToolParams(MethodInfo method, JsonObject arguments, out object?[]? values, out string? errorMessage)
    {
        var parameters = method.GetParameters();
        var argsObj = arguments;

        if (parameters.Length == 1 && !parameters[0].ParameterType.IsSimpleType() && !argsObj.ContainsKey(parameters[0].Name!))
            argsObj = new JsonObject { [parameters[0].Name!] = argsObj };

        var expectedNames = parameters
            .Where(p => p.ParameterType != typeof(CancellationToken) && p.Name != null)
            .Select(p => p.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownKeys = argsObj.Select(kv => kv.Key)
            .Where(k => !expectedNames.Contains(k))
            .ToList();
        if (unknownKeys.Count > 0)
        {
            values = null;
            errorMessage = $"Unknown parameter(s): [{string.Join(", ", unknownKeys)}]. Expected: [{FormatExpectedParams(parameters)}].";
            return false;
        }

        var valuesList = new List<object?>();
        errorMessage = null;

        foreach (var p in parameters)
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;

            if (p.Name == null) continue;
            var node = argsObj[p.Name];

            if (node == null)
            {
                if (p.HasDefaultValue)
                {
                    valuesList.Add(p.DefaultValue);
                }
                else
                {
                    errorMessage = $"parameter '{p.Name}': Missing required parameter '{p.Name}' (type: {p.ParameterType.MapClrTypeToJsonType()}). Expected parameters: [{FormatExpectedParams(parameters)}].";
                    break;
                }
                continue;
            }

            var schema = p.ParameterType.GetSchemaForType();
            var errors = schema.Validate(node, p.Name!);
            if (errors.Any())
            {
                errorMessage = $"parameter '{p.Name}': {string.Join("; ", errors.Select(e => e.Message))}";
                break;
            }

            try
            {
                valuesList.Add(DeserializeNode(node, p.ParameterType));
            }
            catch (Exception ex)
            {
                errorMessage = $"parameter '{p.Name}': {ex.Message}";
                break;
            }
        }

        if (errorMessage != null)
        {
            values = null;
            return false;
        }

        values = valuesList.ToArray();
        return true;
    }

    private static string FormatExpectedParams(ParameterInfo[] parameters)
        => string.Join(", ", parameters
            .Where(p => p.ParameterType != typeof(CancellationToken) && p.Name != null)
            .Select(p =>
            {
                var type = p.ParameterType.MapClrTypeToJsonType();
                var opt = p.HasDefaultValue ? ", optional" : "";
                var underlying = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                var enumVals = underlying.IsEnum 
                    ? $", one of: {string.Join("|", Enum.GetNames(underlying))}" 
                    : "";
                return $"{p.Name} ({type}{opt}{enumVals})";
            }));

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static object? DeserializeNode(JsonNode? node, Type type)
        => JsonSerializer.Deserialize(node, type, _jsonOptions);

    private static object?[] InjectCancellationToken(object?[] toolParams, MethodInfo method, CancellationToken ct)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        int src = 0;

        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];

            if (p.ParameterType == typeof(CancellationToken)) { args[i] = ct; continue; }
            if (src >= toolParams.Length) { args[i] = p.HasDefaultValue ? p.DefaultValue : null; continue; }

            args[i] = toolParams[src++];
        }

        return args;
    }
}
