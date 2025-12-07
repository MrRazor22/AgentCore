using AgentCore.Chat;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Client
{
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

        // SUBCLASS ONLY IMPLEMENTS THIS — returns JSON fragment
        protected abstract JObject GetPayloadJson();
        //just for token counting approximation in case model sdidnt give token usage 
        public string ToPayloadString()
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

            return root.ToString(Formatting.Indented);
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


    public sealed class LLMInitOptions
    {
        public string? BaseUrl { get; set; } = null;
        public string? ApiKey { get; set; } = null;
        public string? Model { get; set; } = null;
    }

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
    public abstract class LLMResponseBase
    {
        public string FinishReason { get; }
        public ToolCall? ToolCall { get; }
        private TokenUsage? _tokenUsage;

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

        public string ToPayloadString()
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

            return root.ToString(Formatting.Indented);
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

    public enum StreamKind
    {
        Text,
        ToolCallDelta,
        Usage,
        Finish,
        // future:
        // Image,
        // Audio,
        // Json,
        // Reasoning
    }

    public readonly struct LLMStreamChunk
    {
        public StreamKind Kind { get; }
        public object? Payload { get; }     // unified extensible payload
        public string? FinishReason { get; }

        public LLMStreamChunk(
            StreamKind kind,
            object? payload = null,
            string? finish = null)
        {
            Kind = kind;
            Payload = payload;
            FinishReason = finish;
        }
    }
    public static class LLMStreamChunkExtensions
    {
        public static TokenUsage? AsTokenUsage(this LLMStreamChunk chunk)
            => chunk.Payload as TokenUsage;
        public static string? AsText(this LLMStreamChunk chunk)
            => chunk.Payload as string;
        public static ToolCall? AsToolCall(this LLMStreamChunk chunk)
            => chunk.Payload as ToolCall;
        public static ToolCallDelta? AsToolCallDelta(this LLMStreamChunk chunk)
            => chunk.Payload as ToolCallDelta;
    }
    public class ToolCallDelta
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Delta { get; set; }
    }
}
