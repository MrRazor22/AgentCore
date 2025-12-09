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

    public sealed class StructuredHandler : BaseChunkHandler
    {
        private static readonly ConcurrentDictionary<Type, JObject> SchemaCache = new ConcurrentDictionary<Type, JObject>();
        private readonly StringBuilder _jsonBuffer = new StringBuilder();
        private LLMStructuredRequest _request;

        public StructuredHandler(
            IToolCallParser parser,
            IToolCatalog tools,
            ILogger<StructuredHandler> logger)
            : base(parser, tools, logger) { }

        public override void OnRequest(LLMRequestBase req)
        {
            _request = (LLMStructuredRequest)req;

            if (!SchemaCache.TryGetValue(_request.ResultType, out var schema))
            {
                schema = _request.ResultType.GetSchemaForType();
                SchemaCache[_request.ResultType] = schema;
            }

            _request.Schema = schema;
        }

        protected override void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text) return;

            var txt = chunk.AsText();
            if (string.IsNullOrEmpty(txt)) return;

            Logger.LogDebug("◄ Stream [Text]: {Text}", txt);

            _jsonBuffer.Append(txt);
        }

        protected override LLMResponseBase OnResponse(ToolCall? firstTool, FinishReason finishReason)
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
                toolCall: firstTool,
                rawJson: json,
                result: result,
                finishReason: finishReason
            );
        }
    }
}