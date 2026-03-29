using AgentCore.Conversation;
using AgentCore.Diagnostics;
using AgentCore.Execution;
using AgentCore.Json;
using AgentCore.Utils;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

public interface IToolExecutor
{
    Task<IContent?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default);
    Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default);
}

public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry _tools;
    private readonly ToolOptions _options;
    private readonly SemaphoreSlim _semaphore;
    private readonly PipelineHandler<ToolCall, Task<ToolResult>> _pipeline;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(
        IToolRegistry tools,
        ToolOptions options,
        ILogger<ToolExecutor> logger,
        IEnumerable<PipelineMiddleware<ToolCall, Task<ToolResult>>>? middlewares = null)
    {
        _tools = tools;
        _options = options;
        _logger = logger;
        _semaphore = new SemaphoreSlim(options.MaxConcurrency);
        
        _pipeline = Pipeline<ToolCall, Task<ToolResult>>.Build(
            middlewares ?? [],
            HandleInternalAsync);
    }
    
    public Task<IContent?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        throw new NotSupportedException("Use HandleToolCallAsync which executes the pipeline instead");
    }

    public Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
        => _pipeline(call, ct);

    private async Task<ToolResult> HandleInternalAsync(ToolCall call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(call.Name))
            return new ToolResult(call.Id, new ToolValidationException("Unknown", "Name", "Tool name cannot be empty."));

        var argsJson = call.Arguments?.ToString() ?? "{}";
        _logger.LogDebug("Executing tool: {Name} Args={ArgsJson}", call.Name, argsJson.Length > 500 ? argsJson[..500] + "..." : argsJson);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        
        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken runCt = ct;
        var sw = Stopwatch.StartNew();

        if (_options.DefaultTimeout.HasValue)
        {
            timeoutCts = new CancellationTokenSource(_options.DefaultTimeout.Value);
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            runCt = linkedCts.Token;
        }

        try
        {
            var result = await InvokeInternalAsync(call, runCt).ConfigureAwait(false);
            sw.Stop();
            _logger.LogDebug("Tool completed: {Name} Duration={Ms}ms", call.Name, sw.ElapsedMilliseconds);
            _logger.LogTrace("Tool result: {Name} Result={Content}", call.Name, result?.ForLlm()?.Length > 200 ? result.ForLlm()[..200] + "..." : result?.ForLlm());
            return new ToolResult(call.Id, result);
        }
        catch (OperationCanceledException ex) when (timeoutCts?.IsCancellationRequested == true)
        {
            sw.Stop();
            _logger.LogWarning("Tool failed: {Name} Error={Message}", call.Name, $"Tool execution timed out after {_options.DefaultTimeout!.Value.TotalSeconds} seconds.");
            return new ToolResult(call.Id, new ToolExecutionException(call.Name, $"Tool execution timed out after {_options.DefaultTimeout!.Value.TotalSeconds} seconds.", ex));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            var wrapped = ex is ToolExecutionException tex ? tex : new ToolExecutionException(call.Name, ex.Message, ex);
            _logger.LogWarning("Tool failed: {Name} Error={Message}", call.Name, wrapped.Message);
            return new ToolResult(call.Id, wrapped);
        }
        finally
        {
            _semaphore.Release();
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }

    private async Task<IContent?> InvokeInternalAsync(ToolCall toolCall, CancellationToken ct)
    {
        var tool = _tools.TryGet(toolCall.Name) ?? throw new ToolExecutionException(toolCall.Name, $"Tool '{toolCall.Name}' not registered.", new InvalidOperationException());

        var method = tool.Method!;
        var toolParams = ParseToolParams(tool.Name, method, toolCall.Arguments);
        var finalArgs = InjectCancellationToken(toolParams, method, ct);

        var rawResult = await tool.Invoker!(finalArgs).ConfigureAwait(false);
        IContent? result = (rawResult is IContent c) ? c : new Text(rawResult.AsJsonString());

        return result;
    }

    private static object?[] ParseToolParams(string toolName, MethodInfo method, JsonObject arguments)
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
            throw new ToolValidationException(toolName, unknownKeys[0],
                $"Unknown parameter(s): [{string.Join(", ", unknownKeys)}]. Expected: [{FormatExpectedParams(parameters)}].");
        }

        var values = new List<object?>();

        foreach (var p in parameters)
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;

            if (p.Name == null) continue;
            var node = argsObj[p.Name];

            if (node == null)
            {
                if (p.HasDefaultValue) values.Add(p.DefaultValue);
                else throw new ToolValidationException(toolName, p.Name!, 
                    $"Missing required parameter '{p.Name}' (type: {p.ParameterType.MapClrTypeToJsonType()}). Expected parameters: [{FormatExpectedParams(parameters)}].");
                continue;
            }

            var schema = p.ParameterType.GetSchemaForType();
            var errors = schema.Validate(node, p.Name!);
            if (errors.Any()) throw new ToolValidationAggregateException(toolName, errors);

            try { values.Add(DeserializeNode(node, p.ParameterType)); }
            catch (Exception ex) { throw new ToolValidationException(toolName, p.Name!, ex.Message); }
        }

        return values.ToArray();
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

public abstract class ToolException : Exception, IContent
{
    public string ToolName { get; }
    protected ToolException(string toolName, string message, Exception? inner = null)
        : base(message, inner)
    {
        ToolName = toolName;
    }
    public virtual string ForLlm()
        => $"Error calling tool '{ToolName}': {Message}";
}
public sealed class ToolExecutionException : ToolException
{
    public ToolExecutionException(string toolName, string message, Exception? inner = null)
        : base(toolName, message, inner) { }
}
public sealed class ToolValidationAggregateException : ToolException
{
    public IReadOnlyList<SchemaValidationError> Errors { get; }
    public ToolValidationAggregateException(string toolName, IEnumerable<SchemaValidationError> errors)
        : base(toolName, "Tool validation failed", new ArgumentException(errors.Select(e => e.Message).ToJoinedString("; ")))
    {
        Errors = errors.ToList();
    }
    public override string ForLlm() 
        => $"Error calling tool '{ToolName}': {string.Join("; ", Errors.Select(e => e.Message))}";
}
public sealed class ToolValidationException : ToolException
{
    public string ParamName { get; }
    public ToolValidationException(string toolName, string paramName, string message)
        : base(toolName, $"{paramName}: {message}") 
    {
        ParamName = paramName;
    }
    public override string ForLlm()
        => $"Error calling tool '{ToolName}', parameter '{ParamName}': {Message}";
}
