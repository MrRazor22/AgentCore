using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using AgentCore.Json;

namespace AgentCore.Tooling;

public sealed class DelegateTool : Tool
{
    private readonly Func<JsonObject, CancellationToken, Task<object?>> _invoker;

    public DelegateTool(Delegate del, string? name = null, string? description = null)
        : base(
            GetName(del, name),
            GetDescription(del, description),
            BuildSchema(del))
    {
        _invoker = CompileInvoker(del.Method, del.Target);
    }

    public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        return _invoker(arguments, ct);
    }

    private static string GetName(Delegate del, string? name)
    {
        ArgumentNullException.ThrowIfNull(del);
        var method = del.Method;
        var declaringType = method.DeclaringType ?? throw new InvalidOperationException("Tool method has no declaring type.");
        var attr = method.GetCustomAttribute<ToolAttribute>();

        return !string.IsNullOrWhiteSpace(name)
            ? name
            : !string.IsNullOrWhiteSpace(attr?.Name)
                ? attr.Name
                : $"{ToSnake(declaringType.Name)}_{ToSnake(method.Name)}";
    }
    private static string ToSnake(string s)
        => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    private static string GetDescription(Delegate del, string? description)
    {
        ArgumentNullException.ThrowIfNull(del);
        var method = del.Method;
        var attr = method.GetCustomAttribute<ToolAttribute>();

        return description
            ?? attr?.Description
            ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? GetName(del, null);
    }



    private static JsonSchema BuildSchema(Delegate del)
    {
        ArgumentNullException.ThrowIfNull(del);
        var method = del.Method;

        if (!IsMethodJsonCompatible(method))
        {
            var declaringType = method.DeclaringType ?? throw new InvalidOperationException("Tool method has no declaring type.");
            throw new InvalidOperationException($"Method is not JSON-compatible: {declaringType.FullName}.{method.Name}");
        }

        var builder = new JsonSchemaBuilder().Type<object>().AdditionalProperties(false);

        foreach (var param in method.GetParameters())
        {
            if (param.ParameterType == typeof(CancellationToken)) continue;
            var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? param.Name!;
            var baseSchema = param.ParameterType.GetSchemaForType();
            var parameterSchema = new JsonSchemaBuilder(baseSchema)
                .Description(desc)
                .Build();

            builder.AddProperty(param.Name!, parameterSchema, required: !param.IsOptional);
        }

        return builder.Build();
    }

    private static bool IsMethodJsonCompatible(MethodInfo m)
    {
        if (m.ContainsGenericParameters || m.ReturnType.ContainsGenericParameters) return false;

        foreach (var p in m.GetParameters())
        {
            var t = p.ParameterType;
            if (t.IsByRef || t.IsPointer || t.ContainsGenericParameters) return false;
        }
        return true;
    }


    internal static Func<JsonObject, CancellationToken, Task<object?>> CompileInvoker(MethodInfo method, object? target)
    {
        var argsParam = Expression.Parameter(typeof(JsonObject), "args");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");
        var parameters = method.GetParameters();
        var argExpressions = new Expression[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.ParameterType == typeof(CancellationToken))
            {
                argExpressions[i] = ctParam;
                continue;
            }

            var getParamMethod = typeof(DelegateTool).GetMethod(nameof(GetParameterValue), BindingFlags.NonPublic | BindingFlags.Static)!;
            var pNameExpr = Expression.Constant(p.Name);
            var pTypeExpr = Expression.Constant(p.ParameterType);
            var pHasDefaultExpr = Expression.Constant(p.HasDefaultValue);
            var pDefaultValueExpr = Expression.Constant(p.DefaultValue, typeof(object));

            var getValueCall = Expression.Call(getParamMethod, argsParam, pNameExpr, pTypeExpr, pHasDefaultExpr, pDefaultValueExpr);
            argExpressions[i] = Expression.Convert(getValueCall, p.ParameterType);
        }

        var instanceObj = target != null ? Expression.Constant(target) : null;
        var call = Expression.Call(instanceObj, method, argExpressions);
        var returnType = method.ReturnType;

        Expression resultExpr;
        if (returnType == typeof(void))
        {
            var block = Expression.Block(call, Expression.Constant(Task.FromResult<object?>(null)));
            resultExpr = block;
        }
        else if (returnType == typeof(Task))
        {
            var helper = typeof(DelegateTool).GetMethod(nameof(CastTaskToTaskObject), BindingFlags.NonPublic | BindingFlags.Static)!;
            resultExpr = Expression.Call(helper, call);
        }
        else if (typeof(Task).IsAssignableFrom(returnType) && returnType.IsGenericType)
        {
            var resultType = returnType.GetGenericArguments()[0];
            var helper = typeof(DelegateTool).GetMethod(nameof(CastGenericTaskToTaskObject), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            resultExpr = Expression.Call(helper, call);
        }
        else
        {
            var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(object));
            resultExpr = Expression.Call(fromResult, Expression.Convert(call, typeof(object)));
        }

        return Expression.Lambda<Func<JsonObject, CancellationToken, Task<object?>>>(resultExpr, argsParam, ctParam).Compile();
    }

    private static object? GetParameterValue(JsonObject obj, string name, Type targetType, bool hasDefault, object? defaultValue)
    {
        if (obj.TryGetPropertyValue(name, out var node) && node != null)
        {
            return DeserializeNode(node, targetType);
        }
        return hasDefault ? defaultValue : (targetType.IsValueType ? Activator.CreateInstance(targetType) : null);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static object? DeserializeNode(JsonNode? node, Type type)
        => JsonSerializer.Deserialize(node, type, _jsonOptions);

    private static async Task<object?> CastTaskToTaskObject(Task t)
    {
        await t.ConfigureAwait(false);
        return null;
    }

    private static async Task<object?> CastGenericTaskToTaskObject<T>(Task<T> t) => await t.ConfigureAwait(false);
}
