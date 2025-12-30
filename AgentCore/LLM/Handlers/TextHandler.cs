using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Client;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using AgentCore.Utils;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public sealed class TextHandler : IChunkHandler
    {
        private readonly ILogger<TextHandler> _logger;
        private readonly IToolCallParser _parser;

        private readonly StringBuilder _buffer = new StringBuilder();
        private ToolCall? _inlineTool;

        public TextHandler(
            IToolCallParser parser,
            ILogger<TextHandler> logger)
        {
            _parser = parser;
            _logger = logger;
        }

        public StreamKind Kind => StreamKind.Text;

        public void OnRequest<T>(LLMRequest<T> request)
        {
            _buffer.Clear();
            _inlineTool = null;
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text)
                return;

            var text = chunk.AsText();
            if (string.IsNullOrEmpty(text))
                return;

            _buffer.Append(text);

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogDebug("◄ Stream [Text]: {Text}", text);

            // detect inline tool call from text
            if (_inlineTool != null)
                return;

            var match = _parser.TryMatch(_buffer.ToString());
            if (match == null)
                return;

            _inlineTool = match;

            // stop streaming immediately
            throw new EarlyStopException("Inline tool call detected.");
        }

        public void OnResponse<T>(LLMResponse<T> response)
        {
            if (_inlineTool != null)
            {
                response.ToolCall = _parser.Validate(_inlineTool);
                return;
            }

            // Text handler only sets Result when T == string
            if (typeof(T) == typeof(string))
            {
                response.Result = (T)(object)_buffer.ToString().Trim();
            }
        }
    }
}
