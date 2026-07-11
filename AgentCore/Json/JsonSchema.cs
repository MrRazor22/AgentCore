using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Json;

public sealed class JsonSchema
{
    private readonly JsonObject _schema;
    private readonly string[] _parameterNames;
    private string? _cachedJson;

    public JsonSchema(JsonObject schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));

        var props = _schema["properties"] as JsonObject;
        _parameterNames = props?.Select(p => p.Key).ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ParameterNames => _parameterNames;

    public IReadOnlyList<string> Validate(JsonNode? node, string path = "")
        => _schema.Validate(node, path);

    public void WriteTo(Utf8JsonWriter writer)
        =>_schema.WriteTo(writer); 

    public JsonNode ToJsonNode() => _schema.DeepClone();
    public override string ToString() => _cachedJson ??= _schema.ToString();
}
