using AgentCore.Chat;
using AgentCore.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgentCore.LLM.Client
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
    public abstract class LLMRequestBase
    {
        public Conversation Prompt { get; internal set; }
        public ToolCallMode ToolCallMode { get; }
        public string? Model { get; }
        public LLMGenerationOptions? Options { get; }
        public IEnumerable<Tool>? ResolvedTools { get; internal set; }

        protected LLMRequestBase(
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

        protected abstract JObject GetPayloadJson();
        public JObject ToPayloadJson()
        {
            var root = new JObject
            {
                ["model"] = Model,
                ["messages"] = JArray.FromObject(Prompt.GetSerializableMessages())
            };

            // Add subclass JSON fragment
            var extra = GetPayloadJson();
            if (extra != null)
            {
                foreach (var prop in extra.Properties())
                    root[prop.Name] = prop.Value;
            }

            AddTools(root);
            AddOptions(root);
            return root;
        }

        private void AddTools(JObject root)
        {
            if (ResolvedTools == null || !ResolvedTools.Any())
                return;

            root["tools"] = new JArray(
                ResolvedTools.Select(t => new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.ParametersSchema
                    }
                })
            );

            root["tool_choice"] = ToolCallMode.ToString().ToLower();
        }
        private void AddOptions(JObject root)  // New: Ensures options (e.g., max_tokens) always wire.
        {
            if (Options == null) return;

            if (Options.Temperature.HasValue) root["temperature"] = Options.Temperature.Value;
            if (Options.TopP.HasValue) root["top_p"] = Options.TopP.Value;
            if (Options.MaxOutputTokens.HasValue) root["max_tokens"] = Options.MaxOutputTokens.Value;
            if (Options.Seed.HasValue) root["seed"] = Options.Seed.Value;
            if (Options.StopSequences?.Any() == true) root["stop"] = JArray.FromObject(Options.StopSequences);
            if (Options.LogitBias?.Any() == true) root["logit_bias"] = JObject.FromObject(Options.LogitBias);
            if (Options.FrequencyPenalty.HasValue) root["frequency_penalty"] = Options.FrequencyPenalty.Value;
            if (Options.PresencePenalty.HasValue) root["presence_penalty"] = Options.PresencePenalty.Value;
            if (Options.TopK.HasValue) root["top_k"] = Options.TopK.Value;
        }
    }

    public sealed class LLMTextRequest : LLMRequestBase
    {
        public LLMTextRequest(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            string? model = null,
            LLMGenerationOptions? options = null)
            : base(prompt, toolCallMode, model, options) { }

        protected override JObject GetPayloadJson() => new JObject();
    }
    public sealed class LLMStructuredRequest : LLMRequestBase
    {
        public Type ResultType { get; }
        public JObject? Schema { get; set; }

        public LLMStructuredRequest(
            Conversation prompt,
            Type resultType,
            ToolCallMode mode,
            string? model = null,
            LLMGenerationOptions? options = null)
            : base(prompt, mode, model, options)
        {
            ResultType = resultType;
        }

        protected override JObject GetPayloadJson()
        {
            if (Schema == null)
                return new JObject();

            return new JObject
            {
                ["response_format"] = new JObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JObject
                    {
                        ["name"] = ResultType.Name,
                        ["strict"] = true,
                        ["schema"] = Schema
                    }
                }
            };
        }
    }
}
