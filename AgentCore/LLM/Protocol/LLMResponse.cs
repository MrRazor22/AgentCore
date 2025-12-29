using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace AgentCore.LLM.Protocol
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AcceptsStreamAttribute : Attribute
    {
        public StreamKind Kind { get; }

        public AcceptsStreamAttribute(StreamKind kind)
        {
            Kind = kind;
        }
    }

    public enum FinishReason
    {
        Stop,
        ToolCall,
        Cancelled
    }

    [AcceptsStream(StreamKind.Text)]
    [AcceptsStream(StreamKind.ToolCallDelta)]
    [AcceptsStream(StreamKind.Usage)]
    [AcceptsStream(StreamKind.Finish)]
    public class LLMResponse
    {
        private TokenUsage? _tokenUsage;

        public string? AssistantMessage { internal set; get; }
        public ToolCall? ToolCall { internal set; get; }
        public FinishReason FinishReason { internal set; get; }

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
        public LLMResponse()
        {
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

    [AcceptsStream(StreamKind.Json)]
    public sealed class LLMStructuredResponse : LLMResponse
    {
        public JToken RawJson { internal set; get; }
        public object? Result { internal set; get; }

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
