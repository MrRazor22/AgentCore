using AgentCore.Chat;
using AgentCore.Tokens;
using Newtonsoft.Json.Linq;
using System;

namespace AgentCore.LLM.Client
{
    public enum FinishReason
    {
        Stop,
        ToolCall,
        Cancelled
    }
    public abstract class LLMResponseBase
    {
        private TokenUsage? _tokenUsage;
        public FinishReason FinishReason { get; }
        public ToolCall? ToolCall { internal set; get; }

        public TokenUsage? TokenUsage
        {
            get => _tokenUsage;
            internal set
            {
                if (_tokenUsage != null)
                    throw new InvalidOperationException("TokenUsage already set");
                _tokenUsage = value;
            }
        }

        protected LLMResponseBase(ToolCall? toolCall, FinishReason finishReason)
        {
            ToolCall = toolCall;
            FinishReason = finishReason;
        }

        // CHILD returns JSON payload for THIS response type
        protected abstract JObject GetPayloadJson();

        public JObject ToPayloadJson()
        {
            var root = GetPayloadJson();

            // Add tool call, if any
            if (ToolCall != null)
            {
                var toolObj = new JObject
                {
                    ["id"] = ToolCall.Id,
                };

                if (!string.IsNullOrEmpty(ToolCall.Name))
                    toolObj["name"] = ToolCall.Name;

                if (ToolCall.Arguments != null)
                    toolObj["arguments"] = ToolCall.Arguments;

                root["tool_call"] = toolObj;
            }

            return root;
        }
    }
    public sealed class LLMTextResponse : LLMResponseBase
    {
        public string? AssistantMessage { get; }

        public LLMTextResponse(
            string? assistantMessage,
            ToolCall? toolCall,
            FinishReason finishReason)
            : base(toolCall, finishReason)
        {
            AssistantMessage = assistantMessage;
        }

        protected override JObject GetPayloadJson()
        {
            return new JObject
            {
                ["message"] = AssistantMessage ?? ""
            };
        }
    }
    public sealed class LLMStructuredResponse : LLMResponseBase
    {
        public JToken RawJson { get; }
        public object? Result { get; }

        public LLMStructuredResponse(
            JToken rawJson,
            object? result,
            FinishReason finishReason = FinishReason.Stop,
            ToolCall? toolCall = null)
            : base(toolCall, finishReason)
        {
            RawJson = rawJson;
            Result = result;
        }

        protected override JObject GetPayloadJson()
        {
            return new JObject
            {
                ["result_json"] = RawJson
            };
        }
    }


}
