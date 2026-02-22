using AgentCore.Json;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Handlers;

public sealed class StructuredHandler(ILogger<StructuredHandler> _logger) : IChunkHandler
{
    private static readonly ConcurrentDictionary<Type, JsonObject> SchemaCache = new();
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };
    private readonly StringBuilder _jsonBuffer = new();
    private JsonObject? _schema;
    private Type? _outputType;

    public StreamKind Kind => StreamKind.Structured;

    public void OnRequest(LLMRequest request)
    {
        _jsonBuffer.Clear();
        _outputType = request.OutputType;
        if (_outputType == null) return;

        _schema = SchemaCache.GetOrAdd(_outputType, t => t.GetSchemaForType());

        _logger.LogDebug("► Request [JsonSchema]: Type={Type}\n{Schema}", _outputType.Name,
            _schema!.ToJsonString(IndentedOptions));
    }

    public void OnChunk(LLMStreamChunk chunk)
    {
        if (chunk.Kind != StreamKind.Structured) return;
        var txt = chunk.AsText();
        if (string.IsNullOrEmpty(txt)) return;
        _logger.LogDebug("◄ Stream [Json]: {Text}", txt);
        _jsonBuffer.Append(txt);
    }

    public void OnResponse(LLMResponse response)
    {
        if (_outputType == null) return;

        var raw = _jsonBuffer.ToString();
        if (string.IsNullOrWhiteSpace(raw)) throw new RetryException("Empty structured response");

        JsonNode? json;
        try { json = JsonNode.Parse(raw); }
        catch { throw new RetryException("Invalid JSON returned by model"); }

        var errors = _schema?.Validate(json, _outputType.Name);
        if (errors?.Count > 0) throw new RetryException("Schema validation failed");

        response.Output = JsonSerializer.Deserialize(json!, _outputType)!;
        _logger.LogDebug("Result [Json]: {Type}", json!.ToJsonString(IndentedOptions));
    }
}
