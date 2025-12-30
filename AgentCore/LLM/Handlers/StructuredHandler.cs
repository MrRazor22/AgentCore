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
    private Type? _resultType;

    private readonly ILogger<StructuredHandler> _logger;

    public StructuredHandler(ILogger<StructuredHandler> logger)
    {
        _logger = logger;
    }

    public StreamKind Kind => StreamKind.Json;

    public void OnRequest<T>(LLMRequest<T> request)
    {
        if (typeof(T) == typeof(string))
            return;

        _resultType = typeof(T);
        if (!SchemaCache.TryGetValue(_resultType, out var schema))
        {
            schema = _resultType.GetSchemaForType();
            SchemaCache[_resultType] = schema;
        }

        _schema = schema;
        request.Schema = schema;

        _logger.LogDebug(
            "► Request [JsonSchema]: Type={Type}\n{Schema}",
            _resultType.Name,
            _schema.ToString(Newtonsoft.Json.Formatting.Indented)
        );

        _jsonBuffer.Clear();
    }

    public void OnChunk(LLMStreamChunk chunk)
    {
        if (chunk.Kind != StreamKind.Json)
            return;

        var txt = chunk.AsText();
        if (string.IsNullOrEmpty(txt))
            return;

        _logger.LogDebug("◄ Stream [Json]: {Text}", txt);
        _jsonBuffer.Append(txt);
    }

    public void OnResponse<T>(LLMResponse<T> response)
    {
        if (typeof(T) == typeof(string))
            return;

        var raw = _jsonBuffer.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            throw new RetryException("Empty JSON response");

        JToken json;
        try { json = JToken.Parse(raw); }
        catch { throw new RetryException("Invalid JSON"); }

        var errors = _schema?.Validate(json, _resultType!.Name);
        if (errors?.Count > 0)
            throw new RetryException("Schema validation failed");

        response.Result = json.ToObject<T>()!;

        _logger.LogDebug(
            "Result [Json]: {Type}", json.ToString(Newtonsoft.Json.Formatting.Indented)
        );
    }
}
