using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Utils;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public interface IToolRuntime
{
    Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default);
    Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default);
}

public sealed class ToolRuntime(IToolCatalog _tools) : IToolRuntime
{
    public async Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));
        ct.ThrowIfCancellationRequested();

        var tool = _tools.Get(toolCall.Name) ?? throw new ToolExecutionException(toolCall.Name, $"Tool '{toolCall.Name}' not registered.", new InvalidOperationException());

        try
        {
            var func = tool.Function;
            var method = func.Method;
            var returnType = method.ReturnType;

            var toolParams = ParseToolParams(method, toolCall.Arguments);
            var finalArgs = InjectCancellationToken(toolParams, method, ct);

            if (typeof(Task).IsAssignableFrom(returnType))
            {
                var task = (Task)func.DynamicInvoke(finalArgs)!;
                await task.ConfigureAwait(false);

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    return returnType.GetProperty("Result")!.GetValue(task);

                return null;
            }

            return func.DynamicInvoke(finalArgs);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not ToolExecutionException) { throw new ToolExecutionException(toolCall.Name, ex.Message, ex); }
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

    private static object[] ParseToolParams(MethodInfo method, JsonObject arguments)
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
                else throw new ToolValidationException(p.Name!, "Missing required parameter.");
                continue;
            }

            var schema = p.ParameterType.GetSchemaForType();
            var errors = schema.Validate(node, p.Name!);
            if (errors.Any()) throw new ToolValidationAggregateException(errors);

            try { values.Add(DeserializeNode(node, p.ParameterType)); }
            catch (Exception ex) { throw new ToolValidationException(p.Name!, ex.Message); }
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

public sealed class ToolExecutionException(string toolName, string message, Exception inner) : Exception(message, inner)
{
    public string ToolName { get; } = toolName;
    public override string ToString() => $"{ToolName}: {Message}";
}

public sealed class ToolValidationAggregateException(IEnumerable<SchemaValidationError> errors) : Exception("Tool validation failed")
{
    public IReadOnlyList<SchemaValidationError> Errors { get; } = errors.ToList();
    public override string ToString() => Errors.Select(e => e.ToString()).ToJoinedString("; ");
}

public sealed class ToolValidationException(string param, string msg) : Exception(msg)
{
    public string ParamName { get; } = param;
    public override string ToString() => $"{ParamName}: {Message}";
}
