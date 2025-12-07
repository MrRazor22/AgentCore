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
    public abstract class BaseChunkHandler : IChunkHandler
    {
        protected readonly IToolCallParser Parser;
        protected readonly IToolCatalog Tools;
        protected readonly ILogger Logger;

        protected readonly StringBuilder Text = new StringBuilder();
        protected readonly StringBuilder ToolArgBuilder = new StringBuilder();

        protected ToolCall? FirstTool;
        protected string? PendingToolId;
        protected string? PendingToolName;

        protected BaseChunkHandler(
            IToolCallParser parser,
            IToolCatalog tools,
            ILogger logger)
        {
            Parser = parser;
            Tools = tools;
            Logger = logger;
        }

        public abstract void PrepareRequest(LLMRequestBase request);

        public void OnChunk(LLMStreamChunk chunk)
        {
            // Centralized tool handling - EVERYONE needs this
            if (chunk.Kind == StreamKind.ToolCallDelta)
            {
                HandleToolDelta(chunk.AsToolCallDelta());
                return;
            }

            // Let derived handlers handle their specific chunks
            HandleSpecificChunk(chunk);
        }

        // Each handler implements its own chunk handling
        protected abstract void HandleSpecificChunk(LLMStreamChunk chunk);

        private void HandleToolDelta(ToolCallDelta? td)
        {
            if (td == null || string.IsNullOrEmpty(td.Delta)) return;

            PendingToolName ??= td.Name;
            PendingToolId ??= td.Id ?? Guid.NewGuid().ToString();

            ToolArgBuilder.Append(td.Delta);

            if (Logger.IsEnabled(LogLevel.Trace))
                Logger.LogTrace("◄ Stream: [{Name}] Args Delta: {Delta}", td.Name, td.Delta);

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

            if (FirstTool == null)
                FirstTool = new ToolCall(PendingToolId!, PendingToolName!, args);

            ToolArgBuilder.Clear();
            PendingToolName = null;
            PendingToolId = null;
        }

        protected ToolCall ValidateTool(ToolCall raw)
        {
            if (!Tools.RegisteredTools.Any(t => t.Name == raw.Name))
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
            if (FirstTool != null)
                FirstTool = ValidateTool(FirstTool);

            return BuildFinalResponse(finishReason);
        }

        protected abstract LLMResponseBase BuildFinalResponse(string finishReason);
    }
}
