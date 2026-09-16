using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Tooling.Tools;

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
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private readonly MethodInvoker _invoker;
    private readonly object? _target;
    private readonly ParameterInfo[] _parameters;
    private readonly Func<Task, object?>? _taskResultGetter;

    public ToolDefinition Info { get; }

    public MethodTool(MethodInfo method, object? target = null, string? name = null, string? description = null, IEnumerable<IMetadata>? extraMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!method.IsStatic && target == null)
            throw new ArgumentException("Instance methods require a target instance.", nameof(target));

        _invoker = MethodInvoker.Create(method);
        _target = target;
        _parameters = method.GetParameters();

        var metadata = method.GetCustomAttributes().OfType<IMetadata>().Concat(extraMetadata ?? []).ToList();
        Info = new(GetName(method, name), GetDescription(method, description), BuildSchema(method), metadata.Count > 0 ? metadata : null);

        if (typeof(Task).IsAssignableFrom(method.ReturnType) && method.ReturnType.IsGenericType)
        {
            var taskParam = Expression.Parameter(typeof(Task), "task");
            var castTask = Expression.Convert(taskParam, method.ReturnType);
            var propAccess = Expression.Property(castTask, "Result");
            var castResult = Expression.Convert(propAccess, typeof(object));
            _taskResultGetter = Expression.Lambda<Func<Task, object?>>(castResult, taskParam).Compile();
        }
    }

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = _parameters.Length == 0 ? [] : new object?[_parameters.Length];
        for (int i = 0; i < _parameters.Length; i++)
        {
            var p = _parameters[i];
            args[i] = p.ParameterType == typeof(CancellationToken) ? ct : GetParameterValue(arguments, p);
        }

        var result = _invoker.Invoke(_target, args);

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            result = _taskResultGetter != null
                ? _taskResultGetter(task)
                : (task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null);
        }

        var contents = ToContentList(result);
        for (int i = 0; i < contents.Count; i++)
            yield return contents[i];
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
