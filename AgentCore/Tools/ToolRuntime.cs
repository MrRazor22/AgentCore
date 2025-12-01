using AgentCore.Chat;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Tools
{
    public interface IToolRuntime
    {
        Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default);
        Task<ToolCallResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default);
    }

    public sealed class ToolRuntime : IToolRuntime
    {
        private readonly IToolCatalog _tools;

        public ToolRuntime(IToolCatalog registry)
        {
            _tools = registry ?? throw new ArgumentNullException(nameof(registry));
        }
        public async Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default)
        {
            if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));
            ct.ThrowIfCancellationRequested();

            var tool = _tools.Get(toolCall.Name);
            if (tool?.Function == null)
                throw new ToolExecutionException(
                    toolCall.Name,
                    $"Tool '{toolCall.Name}' not registered or has no function.",
                    new InvalidOperationException());

            try
            {
                var func = tool.Function;
                var method = func.Method;
                var returnType = method.ReturnType;
                var methodParams = method.GetParameters();
                int expectedJson = methodParams.Count(p => p.ParameterType != typeof(CancellationToken));
                var jsonParams = toolCall.Parameters ?? Array.Empty<object>();

                if (jsonParams.Length != expectedJson)
                    throw new TargetParameterCountException();

                // always inject CT if method wants it
                object?[] finalArgs = InjectCancellationToken(toolCall, method, ct);

                if (typeof(Task).IsAssignableFrom(returnType))
                {
                    ct.ThrowIfCancellationRequested();
                    var task = (Task)func.DynamicInvoke(finalArgs);
                    await task.ConfigureAwait(false);

                    if (returnType.IsGenericType &&
                        returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var resultProperty = returnType.GetProperty("Result")!;
                        return resultProperty.GetValue(task);
                    }
                    return null;
                }

                // synchronous call, CT still injected
                return func.DynamicInvoke(finalArgs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ToolExecutionException(
                    toolCall.Name,
                    $"Failed to invoke tool `{toolCall.Name}`: {ex.Message}",
                    ex);
            }
        }

        private static object?[] InjectCancellationToken(ToolCall call, MethodInfo method, CancellationToken ct)
        {
            var methodParams = method.GetParameters();
            var finalArgs = new object?[methodParams.Length];

            int jsonIndex = 0;
            foreach (var p in methodParams)
            {
                finalArgs[jsonIndex] =
                    p.ParameterType == typeof(CancellationToken)
                    ? ct
                    : call.Parameters[jsonIndex++];
            }

            return finalArgs;
        }

        public async Task<ToolCallResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // text-only assistant message, not a real tool call
            if (string.IsNullOrWhiteSpace(call.Name) &&
                !string.IsNullOrWhiteSpace(call.Message))
            {
                return new ToolCallResult(call, null);
            }

            try
            {
                var result = await InvokeAsync(call, ct).ConfigureAwait(false);
                return new ToolCallResult(call, result);
            }
            catch (ToolExecutionException tex)
            {
                return new ToolCallResult(call, tex);
            }
        }
    }

    public sealed class ToolExecutionException : Exception
    {
        public string ToolName { get; }
        public ToolExecutionException(string toolName, string message, Exception inner)
            : base(message, inner) => ToolName = toolName;
        public override string ToString() => $"Tool '{ToolName}' failed. Reason: '{Message}'";
    }
}
