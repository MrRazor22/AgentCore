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
        private int _lastScanPos = 0;


        public TextHandler(
            IToolCallParser parser,
            IToolCatalog tools,
            ILogger<TextHandler> logger)
            : base(parser, tools, logger) { }

        public override void OnRequest(LLMRequestBase request)
        {
        }

        protected override void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text) return;
            var txt = chunk.AsText();
            if (string.IsNullOrEmpty(txt)) return;

            _text.Append(txt);

            if (Logger.IsEnabled(LogLevel.Trace))
                Logger.LogDebug("◄ Stream [Text]: {Text}", txt);

            var newPortion = _text.ToString(_lastScanPos, _text.Length - _lastScanPos);
            _lastScanPos = _text.Length;

            var inline = Parser.ExtractInlineToolCall(newPortion);
            if (inline.Call == null) return;

            if (_inlineTool == null) _inlineTool = ValidateTool(inline.Call);
            else throw new EarlyStopException("Second inline tool call detected");
        }

        protected override LLMResponseBase OnResponse(ToolCall? toolCall, FinishReason finishReason)
        {
            var finalTool = toolCall ?? _inlineTool;

            Logger.LogInformation("► LLM Response [Text]: {Msg}", _text.ToString().Trim());

            return new LLMTextResponse(
                _text.ToString().Trim(),
                finalTool,
                finishReason
            );
        }
    }
}