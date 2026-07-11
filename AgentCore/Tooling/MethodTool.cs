using AgentCore.Schema;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;


[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute(string? name = null, string? description = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Description { get; } = description;
}

/// <summary>
/// A <see cref="Tool"/> that wraps any C# method via <see cref="MethodInfo"/> + optional target instance.
/// Name, description, and parameter schema are derived automatically from the method signature
/// and <see cref="ToolAttribute"/> / <see cref="DescriptionAttribute"/> annotations.
/// </summary>
public sealed class MethodTool : Tool
{
    private readonly Func<JsonObject, CancellationToken, Task<object?>> _invoker;

    public MethodTool(MethodInfo method, object? target = null, string? name = null, string? description = null)
        : base(
            GetName(method, name),
            GetDescription(method, description),
            BuildSchema(method))
    {
        ArgumentNullException.ThrowIfNull(method);
        _invoker = CompileInvoker(method, target);
    }

    public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
        => _invoker(arguments, ct);

    public static IEnumerable<MethodTool> FromType(Type type, object? instance = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null);

        foreach (var method in methods)
        {
            if (!method.IsStatic && instance == null)
                throw new ArgumentException($"Method '{method.Name}' is an instance method, but no instance was provided.", nameof(instance));

            yield return new MethodTool(method, method.IsStatic ? null : instance);
        }
    }

    // ── Name / description resolution ────────────────────────────────────────

    private static string GetName(MethodInfo method, string? name)
    {
        ArgumentNullException.ThrowIfNull(method);
        var declaringType = method.DeclaringType
            ?? throw new InvalidOperationException("Tool method has no declaring type.");
        var attr = method.GetCustomAttribute<ToolAttribute>();

        return !string.IsNullOrWhiteSpace(name)
            ? name
            : !string.IsNullOrWhiteSpace(attr?.Name)
                ? attr.Name
                : $"{ToSnake(declaringType.Name)}_{ToSnake(method.Name)}";
    }

    private static string ToSnake(string s)
        => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static string GetDescription(MethodInfo method, string? description)
    {
        ArgumentNullException.ThrowIfNull(method);
        var attr = method.GetCustomAttribute<ToolAttribute>();

        return description
            ?? attr?.Description
            ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? GetName(method, null);
    }

    // ── Schema building ───────────────────────────────────────────────────────

    private static JsonSchema BuildSchema(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (!IsMethodJsonCompatible(method))
        {
            var declaringType = method.DeclaringType
                ?? throw new InvalidOperationException("Tool method has no declaring type.");
            throw new InvalidOperationException($"Method is not JSON-compatible: {declaringType.FullName}.{method.Name}");
        }

        var builder = new JsonSchemaBuilder().Type<object>().AdditionalProperties(false);

        foreach (var param in method.GetParameters())
        {
            if (param.ParameterType == typeof(CancellationToken)) continue;
            var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? param.Name!;
            var parameterSchema = new JsonSchemaBuilder(param.ParameterType.GetSchemaForType())
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

    // ── Expression-tree invoker ───────────────────────────────────────────────

    internal static Func<JsonObject, CancellationToken, Task<object?>> CompileInvoker(MethodInfo method, object? target)
    {
        var argsParam = Expression.Parameter(typeof(JsonObject), "args");
        var ctParam   = Expression.Parameter(typeof(CancellationToken), "ct");
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

            var getParamMethod = typeof(MethodTool).GetMethod(nameof(GetParameterValue), BindingFlags.NonPublic | BindingFlags.Static)!;
            var getValueCall = Expression.Call(
                getParamMethod,
                argsParam,
                Expression.Constant(p.Name),
                Expression.Constant(p.ParameterType),
                Expression.Constant(p.HasDefaultValue),
                Expression.Constant(p.DefaultValue, typeof(object)));

            argExpressions[i] = Expression.Convert(getValueCall, p.ParameterType);
        }

        var instanceExpr = target != null ? Expression.Constant(target) : null;
        var call         = Expression.Call(instanceExpr, method, argExpressions);
        var returnType   = method.ReturnType;

        Expression resultExpr;
        if (returnType == typeof(void))
        {
            resultExpr = Expression.Block(call, Expression.Constant(Task.FromResult<object?>(null)));
        }
        else if (returnType == typeof(Task))
        {
            var helper = typeof(MethodTool).GetMethod(nameof(CastTaskToTaskObject), BindingFlags.NonPublic | BindingFlags.Static)!;
            resultExpr = Expression.Call(helper, call);
        }
        else if (typeof(Task).IsAssignableFrom(returnType) && returnType.IsGenericType)
        {
            var resultType = returnType.GetGenericArguments()[0];
            var helper = typeof(MethodTool).GetMethod(nameof(CastGenericTaskToTaskObject), BindingFlags.NonPublic | BindingFlags.Static)!
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
            return DeserializeNode(node, targetType);

        return hasDefault ? defaultValue : (targetType.IsValueType ? Activator.CreateInstance(targetType) : null);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
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
