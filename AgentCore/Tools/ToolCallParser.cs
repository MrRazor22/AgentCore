using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Utils;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public interface IToolCallParser
{
    ToolCall? TryMatch(string content);
    ToolCall Validate(ToolCall toolCall);
}

public sealed class ToolCallParser(IToolCatalog _toolCatalog) : IToolCallParser
{
    public ToolCall? TryMatch(string content)
    {
        foreach (var (start, _, obj) in content.FindAllJsonObjects())
        {
            var name = obj["name"]?.ToString();
            var args = obj["arguments"] as JsonObject;
            if (name == null || args == null || !_toolCatalog.Contains(name)) continue;

            var id = obj["id"]?.ToString() ?? Guid.NewGuid().ToString();
            var prefix = start > 0 ? content.Substring(0, start) : null;

            return new ToolCall(id, name, args);
        }
        return null;
    }

    public ToolCall Validate(ToolCall toolCall)
    {
        var tool = _toolCatalog.Get(toolCall.Name) ?? throw new ToolValidationException(toolCall.Name, "Tool not registered.");
        if (toolCall.Arguments == null) throw new ToolValidationException(toolCall.Name, "Arguments missing.");

        var parsed = ParseToolParams(tool.Function.Method, toolCall.Arguments);
        return new ToolCall(toolCall.Id, toolCall.Name, toolCall.Arguments, parsed);
    }

    private object[] ParseToolParams(MethodInfo method, JsonObject arguments)
    {
        var parameters = method.GetParameters();
        var argsObj = arguments;

        if (parameters.Length == 1 && !parameters[0].ParameterType.IsSimpleType() && !argsObj.ContainsKey(parameters[0].Name!))
            argsObj = new JsonObject { [parameters[0].Name!] = argsObj };

        var values = new List<object?>();

        foreach (var p in parameters)
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;

            var node = argsObj[p.Name];

            if (node == null)
            {
                if (p.HasDefaultValue) values.Add(p.DefaultValue);
                else throw new ToolValidationException(p.Name!, "Missing required parameter.");
                continue;
            }

            var schema = p.ParameterType.GetSchemaForType();
            var errors = schema.Validate(node, p.Name!);
            if (errors.Any()) throw new ToolValidationAggregateException(errors);

            try { values.Add(DeserializeNode(node, p.ParameterType)); }
            catch (Exception ex) { throw new ToolValidationException(p.Name!, ex.Message); }
        }

        return values.ToArray();
    }

    private static object? DeserializeNode(JsonNode? node, Type type)
    {
        return JsonSerializer.Deserialize(node, type);
    }
}

public sealed class ToolValidationAggregateException(IEnumerable<SchemaValidationError> errors) : Exception("Tool validation failed")
{
    public IReadOnlyList<SchemaValidationError> Errors { get; } = errors.ToList();
    public override string ToString() => Errors.Select(e => e.ToString()).ToJoinedString("; ");
}

public sealed class ToolValidationException(string param, string msg) : Exception(msg)
{
    public string ParamName { get; } = param;
    public override string ToString() => $"{ParamName}: {Message}";
}
