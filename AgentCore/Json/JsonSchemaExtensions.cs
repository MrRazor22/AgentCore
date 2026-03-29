using System.Collections.Concurrent;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Json;

public sealed class SchemaValidationError(string param, string? path, string message, string errorType)
{
    public string Param { get; } = param;
    public string? Path { get; } = path;
    public string Message { get; } = message;
    public string ErrorType { get; } = errorType;
}

public static class JsonSchemaExtensions
{
    private static readonly ConcurrentDictionary<Type, JsonObject> _schemaCache = new();

    public static JsonObject GetSchemaFor<T>() => typeof(T).GetSchemaForType();

    public static JsonObject GetSchemaForType(this Type type, HashSet<Type>? visited = null)
    {
        visited ??= [];
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (visited.Count == 0 && _schemaCache.TryGetValue(type, out var cached))
            return (JsonObject)cached.DeepClone();

        var result = BuildSchema(type, visited);

        if (visited.Count == 0)
            _schemaCache.TryAdd(type, (JsonObject)result.DeepClone());

        return result;
    }

    private static JsonObject BuildSchema(Type type, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
        {
            var typeDesc = type.GetCustomAttribute<DescriptionAttribute>()?.Description;
            return new JsonSchemaBuilder()
                .Type<string>()
                .Enum(Enum.GetNames(type))
                .Description(typeDesc ?? $"One of: {string.Join(", ", Enum.GetNames(type))}")
                .Build();
        }

        if (type.IsSimpleType()) return new JsonSchemaBuilder().Type(type.MapClrTypeToJsonType()).Build();
        if (type.IsArray) return new JsonSchemaBuilder().Type<Array>().Items(type.GetElementType()!.GetSchemaForType(visited)).Build();

        if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
            return new JsonSchemaBuilder().Type<Array>().Items(type.GetGenericArguments()[0].GetSchemaForType(visited)).Build();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
            return GetDictionarySchema(type.GetGenericArguments()[1], visited);

        if (visited.Contains(type)) return new JsonSchemaBuilder().Type<object>().Build();

        visited.Add(type);

        var props = new JsonObject();
        var required = new JsonArray();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var propSchema = propType.GetSchemaForType(visited);

            if (prop.GetCustomAttribute<DescriptionAttribute>() is { } descAttr && !string.IsNullOrEmpty(descAttr.Description))
                propSchema[JsonSchemaConstants.DescriptionKey] = descAttr.Description;

            if (prop.GetCustomAttribute<DefaultValueAttribute>() is { } dv) propSchema[JsonSchemaConstants.DefaultKey] = JsonSerializer.SerializeToNode(dv.Value!);

            props[prop.Name] = propSchema;
            if (!prop.IsOptional()) required.Add(prop.Name);
        }

        return new JsonSchemaBuilder().Type<object>().Properties(props).Required(required).AdditionalProperties(false).Build();
    }

    private static JsonObject GetDictionarySchema(Type valueType, HashSet<Type> visited)
        => new JsonSchemaBuilder().Type("object").AdditionalProperties(valueType.GetSchemaForType(visited)).Build();

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

    public static List<SchemaValidationError> Validate(this JsonObject schema, JsonNode? node, string path = "")
    {
        var errors = new List<SchemaValidationError>();

        if (node == null)
        {
            if (schema["required"] is JsonArray arr && arr.Count > 0)
                errors.Add(new SchemaValidationError(path, path, "Value required but missing.", "missing"));
            return errors;
        }

        var type = schema["type"]?.ToString();
        var kind = node.GetValueKind();

        switch (type)
        {
            case "string":
                if (kind != JsonValueKind.String) errors.Add(new SchemaValidationError(path, path, $"Expected string, got {kind}", "type_error"));
                break;
            case "integer":
                if (kind != JsonValueKind.Number) errors.Add(new SchemaValidationError(path, path, $"Expected integer, got {kind}", "type_error"));
                break;
            case "number":
                if (kind != JsonValueKind.Number) errors.Add(new SchemaValidationError(path, path, $"Expected number, got {kind}", "type_error"));
                break;
            case "boolean":
                if (kind != JsonValueKind.True && kind != JsonValueKind.False) errors.Add(new SchemaValidationError(path, path, $"Expected boolean, got {kind}", "type_error"));
                break;
            case "array":
                if (kind != JsonValueKind.Array) errors.Add(new SchemaValidationError(path, path, $"Expected array, got {kind}", "type_error"));
                else if (schema["items"] is JsonObject itemSchema)
                    for (int i = 0; i < node.AsArray().Count; i++)
                        errors.AddRange(itemSchema.Validate(node.AsArray()[i], $"{path}[{i}]"));
                break;
            case "object":
                if (kind != JsonValueKind.Object) errors.Add(new SchemaValidationError(path, path, $"Expected object, got {kind}", "type_error"));
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
                                errors.Add(new SchemaValidationError(key, $"{path}.{key}".Trim('.'), $"Missing required field '{key}'", "missing"));
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
                                errors.Add(new SchemaValidationError(key, $"{path}.{key}".Trim('.'), 
                                    $"Unknown property '{key}'. Expected properties: [{string.Join(", ", schemaKeys)}]", 
                                    "unexpected_key"));
                        }
                    }
                }
                break;
        }

        return errors;
    }
}
