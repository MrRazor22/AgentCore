using AgentCore.Json;
using AgentCore.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

public interface IToolRegistry
{
    IReadOnlyList<Tool> Tools { get; }
    void Register(Delegate del, string? name = null, string? description = null);
    void Register(Tool tool);
    bool Unregister(string toolName);
    Tool? TryGet(string toolName);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, Tool> _registry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ToolRegistry> _logger;
    public IReadOnlyList<Tool> Tools => _registry.Values.ToArray();

    public ToolRegistry(ILogger<ToolRegistry>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ToolRegistry>.Instance;
    }

    public void Register(Delegate del, string? name = null, string? description = null)
    {
        if (del == null) throw new ArgumentException(nameof(del));

        var method = del.Method;

        if (!IsMethodJsonCompatible(method))
            throw new InvalidOperationException($"Method is not JSON-compatible: {FormatMethod(method)}");

        var tool = CreateTool(method, del, name, description);

        if (_registry.ContainsKey(tool.Name))
        {
            var existing = _registry[tool.Name].Method;
            var current = tool.Method;
            _logger.LogWarning("Tool registration failed: ToolName={ToolName} Duplicate - Existing={Existing} Conflicting={Conflicting}",
                tool.Name, FormatMethod(existing), FormatMethod(current));
            throw new InvalidOperationException(
                $"Duplicate tool name '{tool.Name}'. " +
                $"Already registered by {existing?.DeclaringType?.FullName}.{existing?.Name}, " +
                $"conflicts with {current?.DeclaringType?.FullName}.{current?.Name}.");
        }

        _registry[tool.Name] = tool;
        _logger.LogInformation("Tool registered: ToolName={ToolName} DeclaringType={DeclaringType} Method={Method}",
            tool.Name, method.DeclaringType?.FullName, method.Name);
    }

    public void Register(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));

        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("Tool name is required.", nameof(tool));

        if (_registry.ContainsKey(tool.Name))
        {
            _logger.LogWarning("Tool registration failed: ToolName={ToolName} Duplicate", tool.Name);
            throw new InvalidOperationException($"Duplicate tool name '{tool.Name}'.");
        }

        _registry[tool.Name] = tool;
        _logger.LogInformation("Tool registered: ToolName={ToolName}", tool.Name);
    }

    public bool Unregister(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name is required.", nameof(toolName));

        var removed = _registry.TryRemove(toolName, out _);
        _logger.LogDebug("Tool unregistered: ToolName={ToolName} Success={Success}", toolName, removed);
        return removed;
    }

    public Tool? TryGet(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name is required.", nameof(toolName));

        var found = _registry.TryGetValue(toolName, out var entry);
        _logger.LogDebug("Tool lookup: ToolName={ToolName} Found={Found}", toolName, found);
        return entry;
    }

    private static Tool CreateTool(MethodInfo method, Delegate func, string? explicitName, string? explicitDescription)
    {
        var declaringType = method.DeclaringType ?? throw new InvalidOperationException("Tool method has no declaring type.");
        var attr = method.GetCustomAttribute<ToolAttribute>();

        var toolName = !string.IsNullOrWhiteSpace(explicitName)
            ? explicitName
            : !string.IsNullOrWhiteSpace(attr?.Name)
                ? attr!.Name!
                : $"{declaringType.Name.ToSnake()}.{method.Name.ToSnake()}";

        var description = explicitDescription
            ?? attr?.Description
            ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? toolName;

        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in method.GetParameters())
        {
            if (param.ParameterType == typeof(CancellationToken)) continue;

            var name = param.Name!;
            var schema = param.ParameterType.GetSchemaForType();
            var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
            schema[JsonSchemaConstants.DescriptionKey] ??= desc;

            properties[name] = schema;
            if (!param.IsOptional) required.Add(name);
        }

        var schemaObject = new JsonSchemaBuilder()
            .Type<object>()
            .Properties(properties)
            .Required(required)
            .Build();

        return new Tool 
        { 
            Name = toolName, 
            Description = description, 
            ParametersSchema = schemaObject,
            RequiresApproval = attr?.RequiresApproval ?? false,
            Method = method,
            Invoker = CompileInvoker(method, func.Target)
        };
    }

    private static Func<object?[], Task<object?>> CompileInvoker(MethodInfo method, object? target)
    {
        var argsParam = Expression.Parameter(typeof(object?[]), "args");
        var parameters = method.GetParameters();
        var argExpressions = new Expression[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var pType = parameters[i].ParameterType;
            var arrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
            argExpressions[i] = Expression.Convert(arrayAccess, pType);
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
            var helper = typeof(ToolRegistry).GetMethod(nameof(CastTaskToTaskObject), BindingFlags.NonPublic | BindingFlags.Static)!;
            resultExpr = Expression.Call(helper, call);
        }
        else if (typeof(Task).IsAssignableFrom(returnType) && returnType.IsGenericType)
        {
            var resultType = returnType.GetGenericArguments()[0];
            var helper = typeof(ToolRegistry).GetMethod(nameof(CastGenericTaskToTaskObject), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            resultExpr = Expression.Call(helper, call);
        }
        else
        {
            var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(object));
            resultExpr = Expression.Call(fromResult, Expression.Convert(call, typeof(object)));
        }

        return Expression.Lambda<Func<object?[], Task<object?>>>(resultExpr, argsParam).Compile();
    }

    private static async Task<object?> CastTaskToTaskObject(Task t)
    {
        await t.ConfigureAwait(false);
        return null;
    }

    private static async Task<object?> CastGenericTaskToTaskObject<T>(Task<T> t) => await t.ConfigureAwait(false);

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

    private static string FormatMethod(MethodInfo m) => $"{m.DeclaringType?.Name}.{m.Name}";
}
