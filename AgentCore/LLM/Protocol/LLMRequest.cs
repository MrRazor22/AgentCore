using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tools;
using AgentCore.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.LLM.Protocol
{
    public enum ToolCallMode
    {
        None,
        Auto,
        Required
    }

    public sealed class LLMGenerationOptions
    {
        public float? Temperature { get; set; }
        public float? TopP { get; set; }
        public int? MaxOutputTokens { get; set; }
        public int? Seed { get; set; }
        public IReadOnlyList<string>? StopSequences { get; set; }
        public IDictionary<int, int>? LogitBias { get; set; }
        public float? FrequencyPenalty { get; set; }
        public float? PresencePenalty { get; set; }
        public float? TopK { get; set; }
    }

    /// <summary>
    /// Typed LLM request.
    /// T = string by default, custom type enables structured output.
    /// </summary>
    public class LLMRequest<T>
    {
        public Conversation Prompt { get; internal set; }
        public ToolCallMode ToolCallMode { get; }
        public string? Model { get; }
        public LLMGenerationOptions? Options { get; }
        public IEnumerable<Tool>? AvailableTools { get; set; }

        /// <summary>
        /// JSON schema derived from T (null for string).
        /// </summary>
        public JObject? Schema { get; internal set; }

        public LLMRequest(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            string? model = null,
            LLMGenerationOptions? options = null)
        {
            Prompt = prompt;
            ToolCallMode = toolCallMode;
            Model = model;
            Options = options;
        }
        public string ToCountablePayload()
        {
            return string.Concat(
                Prompt.GetSerializableMessages(ChatFilter.All).AsJsonString(),
                AvailableTools.AsJsonString(),
                Schema.AsJsonString()
            );
        }

        public LLMRequest<T> Clone()
            => (LLMRequest<T>)MemberwiseClone();
    }

    /// <summary>
    /// Convenience alias for text requests.
    /// </summary>
    public sealed class LLMRequest : LLMRequest<string>
    {
        public LLMRequest(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            string? model = null,
            LLMGenerationOptions? options = null)
            : base(prompt, toolCallMode, model, options)
        {
        }
    }
}
