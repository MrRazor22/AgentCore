using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Client;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using System;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public sealed class ToolCallHandler : IChunkHandler
    {
        private readonly IToolCatalog _tools;
        private readonly IToolCallParser _parser;
        private readonly StringBuilder _argBuilder = new StringBuilder();
        private ToolCall? _firstTool;
        private string? _pendingToolId;
        private string? _pendingToolName;

        public ToolCallHandler(IToolCatalog tools, IToolCallParser parser)
        {
            _tools = tools;
            _parser = parser;
        }
        public StreamKind Kind => StreamKind.ToolCallDelta;
        public void OnRequest(LLMRequest request)
        {
            _firstTool = null;
            _pendingToolId = null;
            _pendingToolName = null;
            _argBuilder.Clear();

            request.AvailableTools = _tools.RegisteredTools;
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.ToolCallDelta)
                return;

            var td = chunk.AsToolCallDelta();
            if (td == null || string.IsNullOrEmpty(td.Delta))
                return;

            _pendingToolName ??= td.Name;
            _pendingToolId ??= td.Id ?? Guid.NewGuid().ToString();
            _argBuilder.Append(td.Delta);

            var raw = _argBuilder.ToString();
            if (!raw.TryParseCompleteJson(out var json))
                return;

            if (_firstTool != null)
                throw new EarlyStopException("Second tool call detected.");

            if (!_tools.Contains(_pendingToolName!))
                throw new RetryException($"{_pendingToolName}: invalid tool");

            _firstTool = new ToolCall(
                _pendingToolId!,
                _pendingToolName!,
                json!
            );

            _argBuilder.Clear();
            _pendingToolId = null;
            _pendingToolName = null;
        }

        public void OnResponse(LLMResponse response)
        {
            if (_firstTool != null)
            {
                response.ToolCall = _parser.Validate(_firstTool);
            }
        }
    }
}
