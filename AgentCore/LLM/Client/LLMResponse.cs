using AgentCore.Chat;
using AgentCore.Tokens;
using Newtonsoft.Json.Linq;
using System;

namespace AgentCore.LLM.Client
{
    public abstract class LLMResponseBase
    {
        private TokenUsage? _tokenUsage;
        public string FinishReason { get; }
        public ToolCall? ToolCall { get; }

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

        protected LLMResponseBase(ToolCall? toolCall, string finishReason)
        {
            ToolCall = toolCall;
            FinishReason = finishReason ?? "stop";
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
            string finishReason)
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
            ToolCall? toolCall,
            string finishReason)
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
