using System.Text.Json.Nodes;

namespace AgentCore.Json;

public static class JsonSchemaConstants
{
    public const string TypeKey = "type";
    public const string PropertiesKey = "properties";
    public const string RequiredKey = "required";
    public const string DescriptionKey = "description";
    public const string EnumKey = "enum";
    public const string AdditionalPropertiesKey = "additionalProperties";
    public const string ItemsKey = "items";
    public const string DefaultKey = "default";
}

public class JsonSchemaBuilder(JsonObject? existingSchema = null)
{
    private readonly JsonObject _schema = existingSchema ?? new JsonObject();

    public JsonSchemaBuilder Type(string type) { _schema[JsonSchemaConstants.TypeKey] = type; return this; }
    public JsonSchemaBuilder Type<T>() { _schema[JsonSchemaConstants.TypeKey] = typeof(T).MapClrTypeToJsonType(); return this; }
    public JsonSchemaBuilder Properties(JsonObject properties) { _schema[JsonSchemaConstants.PropertiesKey] = properties; return this; }
    public JsonSchemaBuilder Required(JsonArray required) { if (required?.Count > 0) _schema[JsonSchemaConstants.RequiredKey] = required; return this; }
    public JsonSchemaBuilder Description(string description) { if (!string.IsNullOrWhiteSpace(description)) _schema[JsonSchemaConstants.DescriptionKey] = description; return this; }
    public JsonSchemaBuilder Enum(string[] values) { _schema[JsonSchemaConstants.EnumKey] = new JsonArray(values.Select(v => JsonValue.Create(v)).ToArray()); return this; }
    public JsonSchemaBuilder AdditionalProperties(bool allow) { _schema[JsonSchemaConstants.AdditionalPropertiesKey] = allow; return this; }
    public JsonSchemaBuilder AdditionalProperties(JsonObject additionalProps) { _schema[JsonSchemaConstants.AdditionalPropertiesKey] = additionalProps; return this; }
    public JsonSchemaBuilder Items(JsonNode items) { _schema[JsonSchemaConstants.ItemsKey] = items; return this; }
    public JsonObject Build() => _schema;
}
