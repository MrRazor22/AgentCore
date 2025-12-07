using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Client;
using AgentCore.LLM.Pipeline;
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
    public delegate TextHandler TextHandlerFactory();

    public sealed class TextHandler : BaseChunkHandler
    {
        private readonly StringBuilder _text = new StringBuilder();
        private ToolCall? _inlineTool; // Track inline tool locally

        public TextHandler(
            IToolCallParser parser,
            IToolCatalog tools,
            ILogger<TextHandler> logger)
            : base(parser, tools, logger) { }

        public override void OnRequest(LLMRequestBase request) { }

        protected override void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text) return;
            var txt = chunk.AsText();
            if (string.IsNullOrEmpty(txt)) return;

            _text.Append(txt);

            if (Logger.IsEnabled(LogLevel.Trace))
                Logger.LogTrace("◄ Stream Text: {Text}", txt);

            // Check for inline tool calls (only if we haven't found one yet)
            if (_inlineTool == null)
            {
                var inline = Parser.ExtractInlineToolCall(_text.ToString());
                if (inline.Call != null)
                    _inlineTool = inline.Call;
            }
        }

        protected override LLMResponseBase OnResponse(ToolCall? toolCall, string finishReason)
        {
            // Prefer the parameter (from base's tool call delta handling)
            // Fall back to inline tool if no native tool call was detected
            var finalTool = toolCall ?? _inlineTool;

            return new LLMTextResponse(
                _text.ToString().Trim(),
                finalTool,
                finishReason
            );
        }
    }
}