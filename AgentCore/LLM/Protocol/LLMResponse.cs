using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Linq;

namespace AgentCore.LLM.Protocol
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum FinishReason
    {
        Stop,
        ToolCall,
        Cancelled
    }

    /// <summary>
    /// Typed LLM response.
    /// T = string for normal text, custom type for structured output.
    /// </summary>
    public class LLMResponse<T>
    {
        private TokenUsage? _tokenUsage;

        /// <summary>
        /// Final model output.
        /// For tool calls, this may be default(T).
        /// </summary>
        public T Result { get; internal set; } = default!;

        /// <summary>
        /// Optional tool call chosen by the model.
        /// </summary>
        public ToolCall? ToolCall { get; internal set; }

        public FinishReason FinishReason { get; internal set; }

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

        public bool HasToolCall => ToolCall != null;

        public override string ToString()
        {
            return new object?[]
            {
                FinishReason,
                Result,
                ToolCall,
                TokenUsage
            }
            .Where(x => x != null)
            .Select(x => x.AsPrettyJson())
            .ToJoinedString("\n");
        }
    }

    /// <summary>
    /// Convenience alias for text responses.
    /// </summary>
    public sealed class LLMResponse : LLMResponse<string>
    {
    }
}
