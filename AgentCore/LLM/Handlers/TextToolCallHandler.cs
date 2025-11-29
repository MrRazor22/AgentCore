using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.LLM.Pipeline;
using AgentCore.Tokens;
using AgentCore.Tools;
using AgentCore.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public delegate TextToolCallHandler TextHandlerFactory();
    public sealed class TextToolCallHandler : IChunkHandler
    {
        private readonly ILogger<TextToolCallHandler> _logger;
        private readonly IToolCallParser _parser;
        private readonly IToolCatalog _tools;

        private LLMRequest _request;
        private readonly StringBuilder _text = new StringBuilder();
        private ToolCall? _firstTool;

        public TextToolCallHandler(
        IToolCallParser parser,
        IToolCatalog tools,
        ILogger<TextToolCallHandler> logger)
        {
            _parser = parser;
            _tools = tools;
            _logger = logger;
        }

        public void PrepareRequest(LLMRequestBase request)
        {
            _request = (LLMRequest)request;

            _request.AllowedTools =
                _request.ToolCallMode == ToolCallMode.Disabled
                    ? Array.Empty<Tool>()
                    : _request.AllowedTools?.Any() == true
                        ? _request.AllowedTools.ToArray()
                        : _tools.RegisteredTools.ToArray();

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("► Outbound Messages:\n{Json}", _request.Prompt.ToJson());
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            switch (chunk.Kind)
            {
                case StreamKind.Text:
                    {
                        var txt = chunk.AsText();
                        if (string.IsNullOrEmpty(txt)) return;

                        // EXACT same repeat protection
                        CheckRepeat(txt);

                        _text.Append(txt);

                        // inbound text log moved here 1:1
                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("◄ Inbound Stream: {Text}", _text.ToString());

                        // same inline detection
                        var inline = _parser.ExtractInlineToolCall(txt);
                        if (inline.Call != null && _firstTool == null)
                            _firstTool = ValidateTool(inline.Call);

                        break;
                    }

                case StreamKind.ToolCallDelta:
                    {
                        var td = chunk.AsToolCallDelta();
                        if (td == null || string.IsNullOrEmpty(td.Delta)) return;

                        _text.Append(td.Delta);

                        // inbound tool-delta log moved here 1:1
                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("◄ [{Name}] Args: {Args}", td.Name, _text.ToString());

                        break;
                    }

                case StreamKind.ToolCall:
                    {
                        if (_firstTool != null) return;
                        var raw = chunk.AsToolCall();
                        if (raw != null)
                            _firstTool = ValidateTool(raw);
                        break;
                    }
            }
        }

        public LLMResponseBase BuildResponse(string finishReason, TokenUsage? tokenUsage)
        {
            return new LLMResponse(
                _text.ToString().Trim(),
                _firstTool,
                finishReason,
                tokenUsage
            );

        }

        private void CheckRepeat(string text)
        {
            if (_request.Prompt.IsLastAssistantMessageSame(text))
                throw new RetryException("You repeated the same assistant response.");
        }

        private ToolCall ValidateTool(ToolCall raw)
        {
            if (!_tools.RegisteredTools.Any(t => t.Name == raw.Name))
                throw new RetryException($"Tool `{raw.Name}` is invalid.");

            try
            {
                var parsed = _parser.ParseToolParams(raw.Name, raw.Arguments);

                return new ToolCall(
                    raw.Id ?? Guid.NewGuid().ToString(),
                    raw.Name,
                    raw.Arguments,
                    parsed
                );
            }
            catch (ToolValidationAggregateException ex)
            {
                throw new RetryException("Invalid arguments: " +
                    ex.Errors.Select(e => e.Message).ToJoinedString("; "));
            }
            catch (ToolValidationException ex)
            {
                throw new RetryException(ex.Message);
            }
        }
    }

}

