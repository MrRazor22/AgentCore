using AgentCore.Chat;
using System.Reflection;

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
            var toolParams = toolCall.Parameters ?? [];
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
        catch (Exception ex) { throw new ToolExecutionException(toolCall.Name, ex.Message, ex); }
    }

    public async Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(call.Name) && !string.IsNullOrWhiteSpace(call.Message))
            return new ToolCallResult(call, null);

        try
        {
            var result = await InvokeAsync(call, ct).ConfigureAwait(false);
            return new ToolCallResult(call, result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var wrapped = ex is ToolExecutionException tex ? tex : new ToolExecutionException(call.Name, ex.Message, ex);
            return new ToolCallResult(call, wrapped);
        }
    }

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
