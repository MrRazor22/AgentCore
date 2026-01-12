using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;

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
    /// Public, stable LLM response protocol.
    /// No generics. No structured assumptions.
    /// </summary>
    public sealed class LLMResponse
    {
        private TokenUsage? _tokenUsage;

        /// <summary>
        /// Final textual output from the model.
        /// Multimodal extensions can add more fields later.
        /// </summary>
        public string? Text { get; internal set; }

        /// <summary>
        /// Tool call produced by the model (executor-level).
        /// Usually null at agent boundary.
        /// </summary>
        public ToolCall? ToolCall { get; internal set; }
        public object? Output { get; set; }

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

        public string ToCountablePayload()
        {
            return string.Concat(
                Text.AsJsonString(),
                Output?.AsJsonString(),
                ToolCall?.AsJsonString()
                );
        }
    }
}
