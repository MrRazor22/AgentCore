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
        private LLMTextRequest _request;

        public TextHandler(
            IToolCallParser parser,
            IToolCatalog tools,
            ILogger<TextHandler> logger)
            : base(parser, tools, logger) { }

        public override void PrepareSpecificRequest(LLMRequestBase request) { }

        protected override void HandleSpecificChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Text) return;

            var txt = chunk.AsText();
            if (string.IsNullOrEmpty(txt)) return;

            _text.Append(txt);

            if (Logger.IsEnabled(LogLevel.Trace))
                Logger.LogTrace("◄ Stream: {Text}", txt);

            // Check for inline tool calls
            var inline = Parser.ExtractInlineToolCall(_text.ToString());
            if (inline.Call != null && FirstTool == null)
                FirstTool = inline.Call;
        }

        protected override LLMResponseBase BuildFinalResponse(string finishReason)
        {
            return new LLMTextResponse(
                _text.ToString().Trim(),
                FirstTool,
                finishReason
            );
        }
    }
}