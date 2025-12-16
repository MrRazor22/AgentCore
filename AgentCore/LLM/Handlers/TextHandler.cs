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

            if (_inlineTool != null) return;

            var match = Parser.TryMatch(_text.ToString());
            if (match == null) return;

            _inlineTool = ValidateTool(match.Call);

            // cut tool syntax from visible text
            _text.Length = match.Start;
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