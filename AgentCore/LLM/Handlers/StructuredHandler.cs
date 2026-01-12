using AgentCore.Json;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;

public sealed class StructuredHandler : IChunkHandler
{
    private static readonly ConcurrentDictionary<Type, JObject> SchemaCache = new ConcurrentDictionary<Type, JObject>();
    private readonly StringBuilder _jsonBuffer = new StringBuilder();

    private JObject? _schema;
    private Type? _outputType;

    private readonly ILogger<StructuredHandler> _logger;

    public StructuredHandler(ILogger<StructuredHandler> logger)
    {
        _logger = logger;
    }

    public StreamKind Kind => StreamKind.Structured;

    public void OnRequest(LLMRequest request)
    {
        _jsonBuffer.Clear();

        _outputType = request.OutputType;
        if (_outputType == null)
            return;

        if (!SchemaCache.TryGetValue(_outputType, out var schema))
        {
            schema = _outputType.GetSchemaForType();
            SchemaCache[_outputType] = schema;
        }

        _schema = schema;

        _logger.LogDebug(
            "► Request [JsonSchema]: Type={Type}\n{Schema}",
            _outputType.Name,
            _schema.ToString(Newtonsoft.Json.Formatting.Indented)
        );

        _jsonBuffer.Clear();
    }

    public void OnChunk(LLMStreamChunk chunk)
    {
        if (chunk.Kind != StreamKind.Structured)
            return;

        var txt = chunk.AsText();
        if (string.IsNullOrEmpty(txt))
            return;

        _logger.LogDebug("◄ Stream [Json]: {Text}", txt);
        _jsonBuffer.Append(txt);
    }

    public void OnResponse(LLMResponse response)
    {
        if (_outputType == null)
            return;

        var raw = _jsonBuffer.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            throw new RetryException("Empty structured response");

        JToken json;
        try
        {
            json = JToken.Parse(raw);
        }
        catch
        {
            throw new RetryException("Invalid JSON returned by model");
        }

        var errors = _schema?.Validate(json, _outputType.Name);
        if (errors?.Count > 0)
            throw new RetryException("Schema validation failed");

        response.Output = json.ToObject(_outputType)!;

        _logger.LogDebug(
            "Result [Json]: {Type}", json.ToString(Newtonsoft.Json.Formatting.Indented)
        );
    }
}
