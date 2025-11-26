using AgentCore.Json;
using AgentCore.LLMCore.Client;
using AgentCore.LLMCore.Pipeline;
using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;

namespace AgentCore.LLMCore.Handlers
{
    internal sealed class StructuredHandler : IChunkHandler
    {
        private readonly IToolCallParser _parser;
        private readonly IToolCatalog _tools;
        private readonly LLMStructuredRequest _request;
        private readonly StringBuilder _jsonBuffer = new StringBuilder();

        // Cache schemas by type
        private static readonly ConcurrentDictionary<string, JObject> _schemaCache =
            new ConcurrentDictionary<string, JObject>();

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
            var req = (LLMStructuredRequest)request;

            string key = req.ResultType.FullName;

            req.Schema = _schemaCache.GetOrAdd(
                key,
                _ => req.ResultType.GetSchemaForType()
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
            if (chunk.Kind != StreamKind.Text) return;

            var txt = chunk.AsText();
            if (!string.IsNullOrEmpty(txt))
                _jsonBuffer.Append(txt);
        }

        public object BuildResponse(string finish, int input, int output)
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

            var errors = _request.Schema.Validate(
                json,
                _request.ResultType.Name
            );

            if (errors.Count > 0)
            {
                var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                throw new RetryException("Validation failed: " + msg);
            }

            // Deserialize into the runtime result type
            object result = json.ToObject(_request.ResultType);

            return new LLMStructuredResponse(
                json,
                result,
                finish,
                input,
                output
            );
        }
    }
}
