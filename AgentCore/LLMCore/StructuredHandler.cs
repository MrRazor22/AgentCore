using AgentCore.Chat;
using AgentCore.JsonSchema;
using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;

namespace AgentCore.LLMCore
{
    internal sealed class StructuredHandler<T> : IChunkHandler
    {
        private readonly IToolCallParser _parser;
        private readonly IToolCatalog _tools;
        private readonly LLMStructuredRequest _request;
        private readonly StringBuilder _jsonBuffer = new StringBuilder();
        private static readonly ConcurrentDictionary<string, JObject> _schemaCache = new ConcurrentDictionary<string, JObject>();

        public StructuredHandler(
            LLMStructuredRequest request,
            IToolCallParser parser,
            IToolCatalog tools)
        {
            _request = request;
            _parser = parser;
            _tools = tools;
        }

        public void PrepareRequest(LLMRequestBase request)
        {
            var req = request as LLMStructuredRequest;
            req.ResultType = typeof(T);

            string key = req.ResultType.FullName;
            req.Schema = _schemaCache.GetOrAdd(
                key,
                _ => typeof(T).GetSchemaForType()
            );

            req.AllowedTools =
                req.ToolCallMode == ToolCallMode.Disabled
                    ? Array.Empty<Tool>()
                    : req.AllowedTools?.Any() == true
                        ? req.AllowedTools.ToArray()
                        : _tools.RegisteredTools.ToArray();
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind == StreamKind.Text)
            {
                var txt = chunk.AsText();
                if (!string.IsNullOrEmpty(txt))
                    _jsonBuffer.Append(txt);
            }
        }

        public object BuildResponse(string finish, int input, int output)
        {
            string rawText = _jsonBuffer.ToString();
            if (string.IsNullOrWhiteSpace(rawText))
                throw new RetryException("Empty response. Return valid JSON.");

            JToken json;
            try
            {
                json = JToken.Parse(rawText);
            }
            catch
            {
                throw new RetryException("Return valid JSON matching the schema.");
            }

            var errors = _parser.ValidateAgainstSchema(
                json,
                _request.Schema,
                _request.ResultType.Name
            );

            if (errors.Count > 0)
            {
                var msg = string.Join("; ", errors.Select(e => e.Path + ": " + e.Message));
                throw new RetryException("Validation failed: " + msg + ". Fix JSON.");
            }

            T result = json.ToObject<T>();

            return new LLMStructuredResponse<T>(
                json,
                result,
                finish,
                input,
                output
            );
        }
    }
}
