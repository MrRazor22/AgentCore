using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tools;
using AgentCore.Utils;
using System;
using System.Collections.Generic;

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
    /// Public, stable LLM request protocol.
    /// No generics. No schema. No structure assumptions.
    /// </summary>
    public sealed class LLMRequest
    {
        public Conversation Prompt { get; internal set; }
        public ToolCallMode ToolCallMode { get; }
        public string? Model { get; }
        public LLMGenerationOptions? Options { get; }
        public IEnumerable<Tool>? AvailableTools { get; set; }
        public Type? OutputType { get; set; }

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
                OutputType?.GetSchemaForType().AsJsonString()
            );
        }

        public LLMRequest Clone()
            => (LLMRequest)MemberwiseClone();
    }
}
