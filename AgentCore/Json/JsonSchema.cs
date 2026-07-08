using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Json;

public sealed class JsonSchema
{
    private readonly JsonObject _schema;
    private readonly string[] _parameterNames;

    public JsonSchema(JsonObject schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));

        var props = _schema["properties"] as JsonObject;
        _parameterNames = props?.Select(p => p.Key).ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ParameterNames => _parameterNames;

    public List<SchemaValidationError> Validate(JsonNode? node, string path = "")
    {
        return _schema.Validate(node, path);
    }

    public void WriteTo(Utf8JsonWriter writer)
    {
        _schema.WriteTo(writer);
    }

    public override string ToString() => _schema.ToString();
}
