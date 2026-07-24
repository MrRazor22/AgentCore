using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Schema;

public static class JsonSchemaExtensions
{
    private static readonly ConcurrentDictionary<Type, JsonObject> _schemaCache = new();

    public static JsonSchema GetSchemaFor<T>() => typeof(T).GetSchemaForType();

    public static JsonSchema GetSchemaForType(this Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (_schemaCache.TryGetValue(type, out var cached)) return new JsonSchema((JsonObject)cached.DeepClone());
        var result = BuildSubSchema(type, []);
        _schemaCache.TryAdd(type, (JsonObject)result.DeepClone());
        return new JsonSchema(result);
    }

    private static JsonObject BuildSubSchema(Type type, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
        {
            var typeDesc = type.GetCustomAttribute<DescriptionAttribute>()?.Description;
            return new JsonSchemaBuilder()
                .Type<string>()
                .Enum(Enum.GetNames(type))
                .Description(typeDesc ?? $"One of: {string.Join(", ", Enum.GetNames(type))}")
                .BuildObject();
        }

        if (type.IsSimpleType())
            return new JsonSchemaBuilder().Type(type.MapClrTypeToJsonType()).BuildObject();

        if (type.IsArray)
            return new JsonSchemaBuilder().Type<Array>().Items(new JsonSchema(BuildSubSchema(type.GetElementType()!, visited))).BuildObject();

        if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
            return new JsonSchemaBuilder().Type<Array>().Items(new JsonSchema(BuildSubSchema(type.GetGenericArguments()[0], visited))).BuildObject();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
            return new JsonSchemaBuilder().Type<Object>().AdditionalProperties(new JsonSchema(BuildSubSchema(type.GetGenericArguments()[1], visited))).BuildObject();

        if (visited.Contains(type))
            return new JsonSchemaBuilder().Type<object>().BuildObject();

        visited.Add(type);

        var props = new JsonObject();
        var required = new JsonArray();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var propSchema = BuildSubSchema(propType, visited);

            if (prop.GetCustomAttribute<DescriptionAttribute>() is { } descAttr && !string.IsNullOrEmpty(descAttr.Description))
                propSchema[JsonSchemaConstants.DescriptionKey] = descAttr.Description;

            if (prop.GetCustomAttribute<DefaultValueAttribute>() is { } dv)
                propSchema[JsonSchemaConstants.DefaultKey] = JsonSerializer.SerializeToNode(dv.Value!);

            props[prop.Name] = propSchema;
            if (!prop.IsOptional()) required.Add(prop.Name);
        }

        return new JsonSchemaBuilder()
            .Type<object>()
            .Properties(props)
            .Required(required)
            .AdditionalProperties(false)
            .BuildObject();
    }

    private static bool IsOptional(this PropertyInfo prop)
        => Nullable.GetUnderlyingType(prop.PropertyType) != null
        || prop.GetCustomAttribute<DefaultValueAttribute>() != null
        || IsNullableReference(prop);

    private static bool IsNullableReference(PropertyInfo prop)
    {
        return new NullabilityInfoContext().Create(prop).WriteState == NullabilityState.Nullable;
    }

    public static bool IsSimpleType(this Type type) =>
        type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid);

    public static string MapClrTypeToJsonType(this Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum) return "string";
        if (type == typeof(string) || type == typeof(char)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        if (type == typeof(void) || type == typeof(DBNull)) return "null";
        if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return "array";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
            return "integer";
        return "object";
    }

    public static List<string> Validate(this JsonObject schema, JsonNode? node, string path = "")
    {
        var errors = new List<string>();

        if (node == null)
        {
            if (schema["required"] is JsonArray arr && arr.Count > 0)
                errors.Add(string.IsNullOrEmpty(path) ? "Value required but missing." : $"Value required at '{path}' but missing.");
            return errors;
        }

        var type = schema["type"]?.ToString();
        var kind = node.GetValueKind();

        switch (type)
        {
            case "string":
                if (kind != JsonValueKind.String)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected string, got {kind}." : $"Expected string at '{path}', got {kind}.");
                break;
            case "integer":
                if (kind != JsonValueKind.Number)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected integer, got {kind}." : $"Expected integer at '{path}', got {kind}.");
                break;
            case "number":
                if (kind != JsonValueKind.Number)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected number, got {kind}." : $"Expected number at '{path}', got {kind}.");
                break;
            case "boolean":
                if (kind != JsonValueKind.True && kind != JsonValueKind.False)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected boolean, got {kind}." : $"Expected boolean at '{path}', got {kind}.");
                break;
            case "array":
                if (kind != JsonValueKind.Array)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected array, got {kind}." : $"Expected array at '{path}', got {kind}.");
                else if (schema["items"] is JsonObject itemSchema)
                    for (int i = 0; i < node.AsArray().Count; i++)
                        errors.AddRange(itemSchema.Validate(node.AsArray()[i], $"{path}[{i}]"));
                break;
            case "object":
                if (kind != JsonValueKind.Object)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected object, got {kind}." : $"Expected object at '{path}', got {kind}.");
                else if (schema["properties"] is JsonObject props)
                {
                    var objNode = node.AsObject();
                    foreach (var kvp in props)
                    {
                        var key = kvp.Key;
                        var childSchema = kvp.Value as JsonObject;

                        if (!objNode.ContainsKey(key))
                        {
                            if (schema["required"] is JsonArray reqArr && reqArr.Any(r => r?.ToString() == key))
                            {
                                var msg = string.IsNullOrEmpty(path) ? $"Missing required parameter '{key}'." : $"Missing required field '{key}' at '{path}'.";
                                errors.Add(msg);
                            }
                        }
                        else
                        {
                            if (childSchema != null)
                                errors.AddRange(childSchema.Validate(objNode[key], $"{path}.{key}".Trim('.')));
                        }
                    }

                    if (schema[JsonSchemaConstants.AdditionalPropertiesKey] is JsonValue ap
                        && ap.GetValue<bool>() == false)
                    {
                        var schemaKeys = props.Select(p => p.Key).ToHashSet();
                        foreach (var key in objNode.Select(k => k.Key))
                        {
                            if (!schemaKeys.Contains(key))
                            {
                                errors.Add(string.IsNullOrEmpty(path)
                                    ? $"Unknown parameter '{key}'. Expected parameters: [{string.Join(", ", schemaKeys)}]."
                                    : $"Unknown property '{key}' at '{path}'. Expected properties: [{string.Join(", ", schemaKeys)}].");
                            }
                        }
                    }
                }
                break;
        }

        return errors;
    }
}
