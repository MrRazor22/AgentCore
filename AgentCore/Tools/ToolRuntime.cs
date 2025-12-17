using AgentCore.Chat;
using Microsoft.Extensions.Options;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Tools
{
    public sealed class ToolRuntimeOptions
    {
        // null = no timeout 
        public TimeSpan? ExecutionTimeout { get; set; } = null;
    }

    public interface IToolRuntime
    {
        Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default);
        Task<ToolCallResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default);
    }

    public sealed class ToolRuntime : IToolRuntime
    {
        private readonly IToolCatalog _tools;
        private readonly ToolRuntimeOptions _options;

        public ToolRuntime(
            IToolCatalog registry,
            IOptions<ToolRuntimeOptions>? options = null)
        {
            _tools = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options?.Value ?? new ToolRuntimeOptions();
        }

        public async Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default)
        {
            if (toolCall == null)
                throw new ArgumentNullException(nameof(toolCall));

            ct.ThrowIfCancellationRequested();

            var tool = _tools.Get(toolCall.Name)
                ?? throw new ToolExecutionException(
                    toolCall.Name,
                    $"Tool '{toolCall.Name}' not registered.",
                    new InvalidOperationException());

            try
            {
                var func = tool.Function;
                var method = func.Method;
                var returnType = method.ReturnType;

                var toolParams = toolCall.Parameters ?? Array.Empty<object>();

                var finalArgs = InjectCancellationToken(
                    toolParams,
                    method,
                    ct);

                if (typeof(Task).IsAssignableFrom(returnType))
                {
                    var task = (Task)func.DynamicInvoke(finalArgs)!;
                    await task.ConfigureAwait(false);

                    if (returnType.IsGenericType &&
                        returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        return returnType
                            .GetProperty("Result")!
                            .GetValue(task);
                    }

                    return null;
                }

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
                    ex.Message,
                    ex);
            }
        }

        public async Task<ToolCallResult> HandleToolCallAsync(
            ToolCall call,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Text-only assistant message
            if (string.IsNullOrWhiteSpace(call.Name) &&
                !string.IsNullOrWhiteSpace(call.Message))
            {
                return new ToolCallResult(call, null);
            }

            try
            {
                object? result;

                if (_options.ExecutionTimeout.HasValue)
                {
                    using var timeoutCts =
                        CancellationTokenSource.CreateLinkedTokenSource(ct);

                    timeoutCts.CancelAfter(_options.ExecutionTimeout.Value);

                    result = await InvokeAsync(call, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    result = await InvokeAsync(call, ct)
                        .ConfigureAwait(false);
                }

                return new ToolCallResult(call, result);
            }
            catch (OperationCanceledException) when (
                _options.ExecutionTimeout.HasValue &&
                !ct.IsCancellationRequested)
            {
                // timeout cancellation (not user / agent cancellation)
                var timeoutEx = new ToolExecutionException(
                    call.Name,
                    $"Tool execution timed out after {_options.ExecutionTimeout}",
                    new TimeoutException());

                return new ToolCallResult(call, timeoutEx);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var wrapped = ex is ToolExecutionException tex
                    ? tex
                    : new ToolExecutionException(call.Name, ex.Message, ex);

                return new ToolCallResult(call, wrapped);
            }
        }

        private static object?[] InjectCancellationToken(
             object[] toolParams,
             MethodInfo method,
             CancellationToken ct)
        {
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            int src = 0;

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];

                if (p.ParameterType == typeof(CancellationToken))
                {
                    args[i] = ct;
                    continue;
                }

                if (src >= toolParams.Length)
                {
                    // missing argument → use default or null
                    args[i] = p.HasDefaultValue ? p.DefaultValue : null;
                    continue;
                }

                args[i] = toolParams[src++];
            }

            return args;
        }
    }

    public sealed class ToolExecutionException : Exception
    {
        public string ToolName { get; }

        public ToolExecutionException(
            string toolName,
            string message,
            Exception inner)
            : base(message, inner)
        {
            ToolName = toolName;
        }

        public override string ToString()
            => $"{ToolName}: {Message}";
    }
}
