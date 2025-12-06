using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Client;
using AgentCore.LLM.Pipeline;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public delegate StructuredHandler StructuredHandlerFactory();
    public sealed class StructuredHandler : IChunkHandler
    {
        private readonly ILogger<StructuredHandler> _logger;
        private readonly IToolCatalog _tools;

        private LLMStructuredRequest? _request;
        private readonly StringBuilder _jsonBuffer = new StringBuilder();
        private static readonly ConcurrentDictionary<Type, JObject> _schemaCache
            = new ConcurrentDictionary<Type, JObject>();

        public StructuredHandler(
            IToolCatalog tools,
            ILogger<StructuredHandler> logger)
        {
            _tools = tools;
            _logger = logger;
        }

        public void PrepareRequest(LLMRequestBase request)
        {
            _request = (LLMStructuredRequest)request;

            var req = (LLMStructuredRequest)request;
            var type = req.ResultType;

            if (!_schemaCache.TryGetValue(type, out var schema))
            {
                schema = type.GetSchemaForType();
                _schemaCache[type] = schema;
            }

            req.Schema = schema;

            req.AllowedTools =
                req.ToolCallMode == ToolCallMode.Disabled
                    ? Array.Empty<Tool>()
                    : req.AllowedTools?.Any() == true
                        ? req.AllowedTools.ToArray()
                        : _tools.RegisteredTools.ToArray();
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text) return;

            var txt = chunk.AsText();
            if (string.IsNullOrEmpty(txt)) return;

            // inbound log moved here exactly
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("◄ Stream: {Text}", txt);

            _jsonBuffer.Append(txt);
        }

        public LLMResponseBase BuildResponse(string finishReason)
        {
            string raw = _jsonBuffer.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                throw new RetryException("Empty response. Return valid JSON.");

            JToken json;
            try
            {
                json = JToken.Parse(raw);
            }
            catch
            {
                throw new RetryException("Return valid JSON matching the schema.");
            }

            var errors = _request?.Schema?.Validate(json, _request.ResultType.Name);

            if (errors?.Count! > 0)
            {
                var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                throw new RetryException("Validation failed: " + msg);
            }

            object? result = json.ToObject(_request?.ResultType!);

            return new LLMStructuredResponse(
                json,
                result,
                finishReason
            );
        }
    }
}
