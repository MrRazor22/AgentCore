using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace AgentCore.LLM.Protocol
{
    public enum FinishReason
    {
        Stop,
        ToolCall,
        Cancelled
    }
    public class LLMResponse
    {
        private TokenUsage? _tokenUsage;

        public string? AssistantMessage { get; }
        public ToolCall? ToolCall { internal set; get; }
        public FinishReason FinishReason { get; }

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

        public LLMResponse(
            string? assistantMessage,
            ToolCall? toolCall,
            FinishReason finishReason)
        {
            AssistantMessage = assistantMessage;
            ToolCall = toolCall;
            FinishReason = finishReason;
        }

        public virtual string ToString()
        {
            return new object?[]
            {
                FinishReason,
                AssistantMessage,
                ToolCall,
                TokenUsage
            }
            .Where(x => x != null)
            .Select(x => x.AsPrettyJson())
            .ToJoinedString("\n");
        }
    }
    public sealed class LLMStructuredResponse : LLMResponse
    {
        public JToken RawJson { get; }
        public object? Result { get; }

        public LLMStructuredResponse(
            JToken rawJson,
            object? result,
            FinishReason finishReason = FinishReason.Stop,
            ToolCall? toolCall = null)
            : base(assistantMessage: null, toolCall, finishReason)
        {
            RawJson = rawJson;
            Result = result;
        }

        public override string ToString()
        {
            return base.ToString()
                 + "\n"
                 + new object?[]
                   {
                       RawJson,
                       Result
                   }
                   .Where(x => x != null)
                   .Select(x => x.AsPrettyJson())
                   .ToJoinedString("\n");
        }
    }
}
