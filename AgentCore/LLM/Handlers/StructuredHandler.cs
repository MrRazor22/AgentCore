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
        private static readonly ConcurrentDictionary<Type, JObject> SchemaCache = new ConcurrentDictionary<Type, JObject>();
        private readonly StringBuilder _jsonBuffer = new StringBuilder();
        private LLMStructuredRequest _request;
        private ILogger<StructuredHandler> Logger { get; }

        public StructuredHandler(ILogger<StructuredHandler> logger)
        {
            Logger = logger;
        }

        public void OnRequest(LLMRequestBase req)
        {
            _request = (LLMStructuredRequest)req;

            if (!SchemaCache.TryGetValue(_request.ResultType, out var schema))
            {
                schema = _request.ResultType.GetSchemaForType();
                SchemaCache[_request.ResultType] = schema;
            }

            _request.Schema = schema;
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text) return;

            var txt = chunk.AsText();
            if (string.IsNullOrEmpty(txt)) return;

            Logger.LogDebug("◄ Stream [Text]: {Text}", txt);

            _jsonBuffer.Append(txt);
        }

        public LLMResponseBase OnResponse(FinishReason finishReason)
        {
            if (finishReason == FinishReason.Cancelled)
                return new LLMStructuredResponse(
                    toolCall: null,
                    rawJson: JValue.CreateNull(),
                    result: null,
                    finishReason: finishReason
                );

            var raw = _jsonBuffer.ToString();
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

            var errors = _request.Schema?.Validate(json, _request.ResultType.Name);
            if (errors?.Count > 0)
            {
                var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                throw new RetryException("Validation failed: " + msg);
            }

            var result = json.ToObject(_request.ResultType);

            Logger.LogInformation("► LLM Response [Structured]: {Msg}", json.AsPrettyJson());

            return new LLMStructuredResponse(
                rawJson: json,
                result: result,
                finishReason: finishReason
            );
        }
    }
}