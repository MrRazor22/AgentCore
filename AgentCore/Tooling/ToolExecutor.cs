using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Utils;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

public interface IToolExecutor
{
    Task<IContent?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default);
    Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default);
}

public sealed class ToolExecutor(IToolRegistry _tools) : IToolExecutor
{
    public Func<ToolCall, CancellationToken, Task<IContent?>>? BeforeCall { get; init; }
    public Func<ToolCall, IContent?, CancellationToken, Task<IContent?>>? AfterCall { get; init; }

    public async Task<IContent?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));
        ct.ThrowIfCancellationRequested();

        var tool = _tools.TryGet(toolCall.Name) ?? throw new ToolExecutionException(toolCall.Name, $"Tool '{toolCall.Name}' not registered.", new InvalidOperationException());

        if (BeforeCall != null)
        {
            var bypass = await BeforeCall(toolCall, ct).ConfigureAwait(false);
            if (bypass != null) return bypass;
        }

        IContent? result;
        try
        {
            var func = tool.Function;
            var method = func.Method;
            var returnType = method.ReturnType;

            var toolParams = ParseToolParams(tool.Name, method, toolCall.Arguments);
            var finalArgs = InjectCancellationToken(toolParams, method, ct);

            if (typeof(Task).IsAssignableFrom(returnType))
            {
                var task = (Task)func.DynamicInvoke(finalArgs)!;
                await task.ConfigureAwait(false);

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var taskResult = returnType.GetProperty("Result")!.GetValue(task);
                    result = (IContent?)(taskResult is IContent ? taskResult : new Text(taskResult.AsJsonString()));
                }
                else
                {
                    result = null;
                }
            }
            else
            {
                var rawResult = func.DynamicInvoke(finalArgs);
                result = (IContent?)(rawResult is IContent ? rawResult : new Text(rawResult.AsJsonString()));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not ToolExecutionException) { throw new ToolExecutionException(toolCall.Name, ex.Message, ex); }

        if (AfterCall != null)
        {
            var replacement = await AfterCall(toolCall, result, ct).ConfigureAwait(false);
            if (replacement != null) return replacement;
        }

        return result;
    }

    public async Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(call.Name))
            return new ToolResult(call.Id, null);

        try
        {
            var result = await InvokeAsync(call, ct).ConfigureAwait(false);
            return new ToolResult(call.Id, result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var wrapped = ex is ToolExecutionException tex ? tex : new ToolExecutionException(call.Name, ex.Message, ex);
            return new ToolResult(call.Id, wrapped);
        }
    }

    private static object[] ParseToolParams(string toolName, MethodInfo method, JsonObject arguments)
    {
        var parameters = method.GetParameters();
        var argsObj = arguments;

        if (parameters.Length == 1 && !parameters[0].ParameterType.IsSimpleType() && !argsObj.ContainsKey(parameters[0].Name!))
            argsObj = new JsonObject { [parameters[0].Name!] = argsObj };

        var values = new List<object?>();

        foreach (var p in parameters)
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;

            var node = argsObj[p.Name];

            if (node == null)
            {
                if (p.HasDefaultValue) values.Add(p.DefaultValue);
                else throw new ToolValidationException(toolName, p.Name!, "Missing required parameter.");
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

    private static object? DeserializeNode(JsonNode? node, Type type)
        => JsonSerializer.Deserialize(node, type);

    private static object?[] InjectCancellationToken(object[] toolParams, MethodInfo method, CancellationToken ct)
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
        => $"{ToolName}: {Message}";
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
    public override string ForLlm() => $"{ToolName}: {Message}";
}
public sealed class ToolValidationException : ToolException
{
    public string ParamName { get; }
    public ToolValidationException(string toolName, string paramName, string message)
        : base(toolName, $"{paramName}: {message}") 
    {
        ParamName = paramName;
    }
}
