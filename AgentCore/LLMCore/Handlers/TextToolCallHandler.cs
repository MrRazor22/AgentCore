using AgentCore.Chat;
using AgentCore.LLMCore.Client;
using AgentCore.LLMCore.Pipeline;
using AgentCore.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgentCore.LLMCore.Handlers
{
    internal sealed class TextToolCallHandler : IChunkHandler
    {
        private readonly IToolCallParser _parser;
        private readonly IToolCatalog _tools;

        private readonly StringBuilder _text = new StringBuilder();
        private ToolCall? _firstTool;
        private Conversation _prompt;

        public TextToolCallHandler(
            IToolCallParser parser,
            IToolCatalog tools,
            Conversation prompt)
        {
            _parser = parser;
            _tools = tools;
            _prompt = prompt;
        }

        public void PrepareRequest(LLMRequestBase request)
        {
            var req = request as LLMRequest;
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
                var text = chunk.AsText();
                if (string.IsNullOrEmpty(text)) return;

                CheckRepeat(text);

                _text.Append(text);

                var inline = _parser.ExtractInlineToolCall(text);
                if (inline.Call != null && _firstTool == null)
                    _firstTool = ValidateTool(inline.Call);

                return;
            }

            if (chunk.Kind == StreamKind.ToolCall)
            {
                if (_firstTool != null) return;

                var raw = chunk.AsToolCall();
                if (raw == null) return;

                _firstTool = ValidateTool(raw);
                return;
            }
        }

        public object BuildResponse(string finish, int input, int output)
        {
            return new LLMResponse(
                _firstTool == null ? _text.ToString().Trim() : null,
                _firstTool,
                finish,
                input,
                output
            );
        }

        private void CheckRepeat(string text)
        {
            if (_prompt.IsLastAssistantMessageSame(text))
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
                    string.Join("; ", ex.Errors.Select(e => e.Message)));
            }
            catch (ToolValidationException ex)
            {
                throw new RetryException(ex.Message);
            }
        }
        public string GetOutputTextForTokenCount(object response)
        {
            var r = (LLMResponse)response;
            return r.AssistantMessage ?? "";
        }

    }

}

