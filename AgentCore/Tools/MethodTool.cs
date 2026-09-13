using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute(string? name = null, string? description = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Description { get; } = description;
}

/// <summary>
/// An <see cref="ITool"/> that wraps any C# method via <see cref="MethodInfo"/> + optional target instance.
/// </summary>
public sealed class MethodTool : ITool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly MethodInfo _method;
    private readonly object? _target;
    private readonly ParameterInfo[] _parameters;

    public ToolDefinition Definition { get; }

    public MethodTool(MethodInfo method, object? target = null, string? name = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!method.IsStatic && target == null)
            throw new ArgumentException("Instance methods require a target instance.", nameof(target));

        _method = method;
        _target = target;
        _parameters = method.GetParameters();
        Definition = new(GetName(method, name), GetDescription(method, description), BuildSchema(method));
    }

    public async IAsyncEnumerable<IBlockEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = new object?[_parameters.Length];
        for (int i = 0; i < _parameters.Length; i++)
        {
            var p = _parameters[i];
            args[i] = p.ParameterType == typeof(CancellationToken) ? ct : GetParameterValue(arguments, p);
        }

        var result = _method.Invoke(_target, args);

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            result = task.GetType().GetProperty("Result")?.GetValue(task);
        }

        var contents = ToContentList(result);
        for (int i = 0; i < contents.Count; i++)
            yield return new ContentEvent(i, contents[i]);
    }

    private static object? GetParameterValue(JsonObject obj, ParameterInfo p)
    {
        if (obj.TryGetPropertyValue(p.Name!, out var node) && node != null)
            return JsonSerializer.Deserialize(node, p.ParameterType, JsonOptions);

        return p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
    }

    private static IReadOnlyList<IContent> ToContentList(object? raw) => raw switch
    {
        null => [new Text(string.Empty)],
        IContent c => [c],
        IReadOnlyList<IContent> list => list,
        IEnumerable<IContent> enumContents => enumContents.ToList(),
        string s => [new Text(s)],
        _ => [new Text(JsonSerializer.Serialize(raw))]
    };

    private static string GetName(MethodInfo method, string? name) =>
        !string.IsNullOrWhiteSpace(name) ? name : method.GetCustomAttribute<ToolAttribute>()?.Name ?? method.Name;

    private static string GetDescription(MethodInfo method, string? description) =>
        description
        ?? method.GetCustomAttribute<ToolAttribute>()?.Description
        ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
        ?? GetName(method, null);

    private static JsonSchema BuildSchema(MethodInfo method)
    {
        var builder = new JsonSchemaBuilder().Type<object>().AdditionalProperties(false);
        builder.Properties(new JsonObject());

        foreach (var p in method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;
            var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? p.Name!;
            var paramSchema = new JsonSchemaBuilder(p.ParameterType.GetSchemaForType()).Description(desc).Build();
            builder.AddProperty(p.Name!, paramSchema, required: !p.IsOptional);
        }

        return builder.Build();
    }
}
