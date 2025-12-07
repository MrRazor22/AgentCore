using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Client;
using AgentCore.LLM.Pipeline;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public interface IChunkHandler
    {
        void PrepareRequest(LLMRequestBase request);
        void HandleChunk(LLMStreamChunk chunk);
        LLMResponseBase BuildResponse(string finishReason);
    }
    public abstract class BaseChunkHandler : IChunkHandler
    {
        private readonly StringBuilder ToolArgBuilder = new StringBuilder();

        private ToolCall? _firstTool;
        private string? _pendingToolId;
        private string? _pendingToolName;

        protected readonly IToolCallParser Parser;
        protected readonly IToolCatalog Tools;
        protected readonly ILogger Logger;

        protected BaseChunkHandler(
            IToolCallParser parser,
            IToolCatalog tools,
            ILogger logger)
        {
            Parser = parser;
            Tools = tools;
            Logger = logger;
        }

        public void PrepareRequest(LLMRequestBase request)
        {
            request.ResolvedTools = Tools.RegisteredTools;
            Logger.LogInformation("► LLM Request: {Msg}", request.Prompt.LastOrDefault().AsPrettyJson());
            OnRequest(request);
        }
        public abstract void OnRequest(LLMRequestBase request);

        public void HandleChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind == StreamKind.ToolCallDelta)
            {
                HandleToolDelta(chunk.AsToolCallDelta());
                return;
            }
            OnChunk(chunk);
        }

        protected abstract void OnChunk(LLMStreamChunk chunk);

        private void HandleToolDelta(ToolCallDelta? td)
        {
            if (td == null || string.IsNullOrEmpty(td.Delta)) return;

            _pendingToolName ??= td.Name;
            _pendingToolId ??= td.Id ?? Guid.NewGuid().ToString();

            ToolArgBuilder.Append(td.Delta);

            if (Logger.IsEnabled(LogLevel.Trace))
                Logger.LogDebug("◄ Stream [ToolCall]: [{Name}] Args Delta: {Delta}", td.Name, td.Delta);

            TryAssembleToolCall();
        }

        private void TryAssembleToolCall()
        {
            if (ToolArgBuilder.Length == 0) return;

            var raw = ToolArgBuilder.ToString();
            if (!raw.TryParseCompleteJson(out _))
                return;

            JObject args;
            try { args = JObject.Parse(raw); }
            catch { args = new JObject(); }

            if (_firstTool == null)
                _firstTool = new ToolCall(_pendingToolId!, _pendingToolName!, args);

            ToolArgBuilder.Clear();
            _pendingToolName = null;
            _pendingToolId = null;
        }

        protected ToolCall ValidateTool(ToolCall raw)
        {
            if (!Tools.Contains(raw.Name))
                throw new RetryException($"{raw.Name}: invalid tool");

            try
            {
                var parsed = Parser.ParseToolParams(raw.Name, raw.Arguments);

                return new ToolCall(
                    raw.Id ?? Guid.NewGuid().ToString(),
                    raw.Name,
                    raw.Arguments,
                    parsed
                );
            }
            catch (Exception ex) when (
                ex is ToolValidationException ||
                ex is ToolValidationAggregateException ||
                ex is ToolExecutionException)
            {
                throw new RetryException(ex.ToString());
            }
        }

        public LLMResponseBase BuildResponse(string finishReason)
        {
            if (_firstTool != null)
                _firstTool = ValidateTool(_firstTool);

            return OnResponse(_firstTool, finishReason);
        }

        protected abstract LLMResponseBase OnResponse(ToolCall? toolCall, string finishReason);
    }
}
