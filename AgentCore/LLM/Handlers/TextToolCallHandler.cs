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
    public delegate TextToolCallHandler TextHandlerFactory();

    public sealed class TextToolCallHandler : IChunkHandler
    {
        private readonly IToolCallParser _parser;
        private readonly IToolCatalog _tools;
        private readonly ILogger<TextToolCallHandler> _logger;

        private LLMRequest _request;

        private readonly StringBuilder _text = new StringBuilder();
        private ToolCall? _firstTool;

        // For delta assembly
        private readonly StringBuilder _toolArgs = new StringBuilder();
        private string? _pendingToolName;
        private string? _pendingToolId;

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
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            switch (chunk.Kind)
            {
                case StreamKind.Text:
                    {
                        var txt = chunk.AsText();
                        if (string.IsNullOrEmpty(txt)) return;

                        _text.Append(txt);

                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("◄ Inbound Stream: {Text}", _text.ToString());

                        // Just store it and validate later when BuildResponse() runs.
                        var inline = _parser.ExtractInlineToolCall(_text.ToString());
                        if (inline.Call != null && _firstTool == null)
                            _firstTool = inline.Call;

                        break;
                    }

                case StreamKind.ToolCallDelta:
                    {
                        var td = chunk.AsToolCallDelta();
                        if (td == null || string.IsNullOrEmpty(td.Delta)) return;

                        _pendingToolName ??= td.Name;
                        _pendingToolId ??= td.Id ?? Guid.NewGuid().ToString();

                        _toolArgs.Append(td.Delta);

                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("◄ [{Name}] Args Delta: {Delta}", td.Name, td.Delta);

                        TryAssembleToolCall();
                        break;
                    }
            }
        }

        public LLMResponseBase BuildResponse(string finishReason)
        {
            if (_firstTool != null)
                _firstTool = ValidateTool(_firstTool);

            return new LLMResponse(
                _text.ToString().Trim(),
                _firstTool,
                finishReason
            );
        }

        private void TryAssembleToolCall()
        {
            if (_toolArgs.Length == 0) return;

            var raw = _toolArgs.ToString();
            if (!raw.TryParseCompleteJson(out _))
                return;

            JObject args;
            try { args = JObject.Parse(raw); }
            catch { args = new JObject(); }

            if (_firstTool == null)
                _firstTool = new ToolCall(_pendingToolId!, _pendingToolName!, args);

            _toolArgs.Clear();
            _pendingToolName = null;
            _pendingToolId = null;
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
                throw new RetryException(
                    "Invalid arguments: " +
                    ex.Errors.Select(e => e.Message).ToJoinedString("; "));
            }
            catch (ToolValidationException ex)
            {
                throw new RetryException(ex.Message);
            }
        }
    }
}
