using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Client;
using AgentCore.Tokens;
using AgentCore.Tools;
using AgentCore.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public sealed class TextHandler : IChunkHandler
    {
        private readonly ILogger<TextHandler> _logger;
        private readonly IToolCallParser _parser;
        private readonly StringBuilder _text = new StringBuilder();
        private ToolCall? _inlineTool;

        public TextHandler(
            IToolCallParser parser,
            ILogger<TextHandler> logger)
        {
            _parser = parser;
            _logger = logger;
        }
        public StreamKind Kind => StreamKind.Text;
        public void OnRequest(LLMRequest request)
        {
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text) return;

            var txt = chunk.AsText();
            if (string.IsNullOrEmpty(txt)) return;

            _text.Append(txt);

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogDebug("◄ Stream [Text]: {Text}", txt);

            if (_inlineTool != null) return;

            var match = _parser.TryMatch(_text.ToString());
            if (match == null) return;

            _inlineTool = match;   // RAW ToolCall only

            throw new EarlyStopException("Inline tool call detected.");
        }

        public void OnResponse(LLMResponse response)
        {
            if (_inlineTool != null)
            {
                response.AssistantMessage = _inlineTool.Message;
                response.ToolCall = _parser.Validate(_inlineTool);
            }
            else
                response.AssistantMessage = _text.ToString().Trim();
        }
    }
}