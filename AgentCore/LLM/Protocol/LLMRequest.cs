using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tools;
using AgentCore.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgentCore.LLM.Protocol
{
    public enum ToolCallMode
    {
        None,     // expose tools but forbid calls
        Auto,     // allow text or tool calls
        Required  // force tool call 
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
    public class LLMRequest
    {
        public Conversation Prompt { get; internal set; }
        public ToolCallMode ToolCallMode { get; }
        public string? Model { get; }
        public LLMGenerationOptions? Options { get; }
        public IEnumerable<Tool>? AvailableTools { get; internal set; }

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

        public virtual string ToString()
        {
            return new object?[]
            {
                //Model,
                //ToolCallMode,
                //Options,
                AvailableTools,
                Prompt.ToJson()
            }
            .Where(x => x != null)
            .Select(x => x is string s ? s : x.AsPrettyJson())
            .ToJoinedString("\n");
        }
        public LLMRequest Clone()
        {
            return (LLMRequest)this.MemberwiseClone();
        }
    }

    public sealed class LLMStructuredRequest : LLMRequest
    {
        public Type ResultType { get; }
        public JObject? Schema { get; set; }

        public LLMStructuredRequest(
            Conversation prompt,
            Type resultType,
            ToolCallMode mode = ToolCallMode.Auto,
            string? model = null,
            LLMGenerationOptions? options = null)
            : base(prompt, mode, model, options)
        {
            ResultType = resultType;
        }

        public override string ToString()
        {
            return base.ToString()
                 + "\n"
                 + Schema.AsPrettyJson();
        }
        public LLMStructuredRequest Clone()
        {
            return (LLMStructuredRequest)this.MemberwiseClone();
        }
    }
}
