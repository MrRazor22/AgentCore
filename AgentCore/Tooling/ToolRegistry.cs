using AgentCore.Json;
using AgentCore.Utils;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tooling;

public interface IToolRegistry
{
    IReadOnlyList<Tool> Tools { get; }
    void Register(Delegate del, string? name = null, string? description = null);
    bool Unregister(string toolName);
    Tool? TryGet(string toolName);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, Tool> _registry = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<Tool> Tools => _registry.Values.ToArray();

    public void Register(Delegate del, string? name = null, string? description = null)
    {
        if (del == null) throw new ArgumentException(nameof(del));

        var method = del.Method;

        if (!IsMethodJsonCompatible(method))
            throw new InvalidOperationException($"Method is not JSON-compatible: {FormatMethod(method)}");

        var tool = CreateTool(method, del, name, description);

        if (_registry.ContainsKey(tool.Name))
            throw new InvalidOperationException(
                $"Duplicate tool name '{tool.Name}'. " +
                $"Already registered by {_registry[tool.Name].Function?.Method.DeclaringType?.FullName}." +
                $"{_registry[tool.Name].Function?.Method.Name}, " +
                $"conflicts with {tool.Function?.Method.DeclaringType?.FullName}." +
                $"{tool.Function?.Method.Name}.");

        _registry[tool.Name] = tool;
    }

    public bool Unregister(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name is required.", nameof(toolName));

        return _registry.TryRemove(toolName, out _);
    }

    public Tool? TryGet(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name is required.", nameof(toolName));

        return _registry.TryGetValue(toolName, out var entry) ? entry : null;
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

        return new Tool { Name = toolName, Description = description, ParametersSchema = schemaObject, Function = func };
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

    private static string FormatMethod(MethodInfo m) => $"{m.DeclaringType?.Name}.{m.Name}";
}
