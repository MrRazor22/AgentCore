using AgentCore.Schema;
using System.ComponentModel;
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
    private readonly MethodInfo _method;
    private readonly object? _target;
    private readonly ParameterInfo[] _parameters;
    private readonly bool _returnsTask;
    private readonly bool _returnsGenericTask;
    private readonly PropertyInfo? _taskResultProperty;

    public MethodTool(MethodInfo method, object? target = null, string? name = null, string? description = null)
        : base(
            GetName(method, name),
            GetDescription(method, description),
            BuildSchema(method))
    {
        ArgumentNullException.ThrowIfNull(method);
        
        if (!method.IsStatic && target == null)
            throw new ArgumentException("Instance methods require a target instance.", nameof(target));

        _method = method;
        _target = target;
        _parameters = method.GetParameters();

        _returnsTask = method.ReturnType == typeof(Task);
        _returnsGenericTask = method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);
        
        if (_returnsGenericTask)
        {
            _taskResultProperty = method.ReturnType.GetProperty("Result");
        }
    }

    public override async Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var args = new object?[_parameters.Length];

        for (int i = 0; i < _parameters.Length; i++)
        {
            var p = _parameters[i];
            if (p.ParameterType == typeof(CancellationToken))
            {
                args[i] = ct;
                continue;
            }

            args[i] = GetParameterValue(arguments, p.Name!, p.ParameterType, p.HasDefaultValue, p.DefaultValue);
        }

        var result = _method.Invoke(_target, args);

        if (_returnsTask)
        {
            await (Task)result!;
            return null;
        }

        if (_returnsGenericTask)
        {
            var task = (Task)result!;
            await task.ConfigureAwait(false);
            return _taskResultProperty!.GetValue(task);
        }

        return result;
    }

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

    private static string GetName(MethodInfo method, string? name)
    {
        ArgumentNullException.ThrowIfNull(method);
        var attr = method.GetCustomAttribute<ToolAttribute>();

        return !string.IsNullOrWhiteSpace(name)
            ? name
            : !string.IsNullOrWhiteSpace(attr?.Name)
                ? attr.Name
                : method.Name;
    }

    private static string GetDescription(MethodInfo method, string? description)
    {
        ArgumentNullException.ThrowIfNull(method);
        var attr = method.GetCustomAttribute<ToolAttribute>();

        return description
            ?? attr?.Description
            ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? GetName(method, null);
    }

    private static JsonSchema BuildSchema(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

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
}
