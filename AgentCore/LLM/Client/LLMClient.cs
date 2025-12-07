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
    public interface IChunkHandler
    {
        void PrepareRequest(LLMRequestBase request);
        void OnChunk(LLMStreamChunk chunk);
        LLMResponseBase BuildResponse(string finishReason);
    }
    public abstract class LLMRequestBase
    {
        public Conversation Prompt { get; internal set; }
        public string? Model { get; }
        public LLMGenerationOptions? Options { get; }
        protected LLMRequestBase(
            Conversation prompt,
            string? model = null,
            LLMGenerationOptions? options = null)
        {
            Prompt = prompt;
            Model = model;
            Options = options;
        }
        public abstract string ToSerializablePayload();
        public abstract LLMRequestBase DeepClone();
    }
    public class LLMTextRequest : LLMRequestBase
    {
        public ToolCallMode ToolCallMode { get; }
        public IEnumerable<Tool>? AllowedTools { get; internal set; }

        public LLMTextRequest(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            IEnumerable<Tool>? allowedTools = null,
            string? model = null,
            LLMGenerationOptions? options = null)
            : base(prompt, model, options)
        {
            AllowedTools = allowedTools;
            ToolCallMode = toolCallMode;
        }
        public override string ToSerializablePayload()
        {
            var root = new JObject
            {
                ["model"] = Model,
                ["messages"] = JArray.FromObject(Prompt.GetSerializableMessages())
            };

            if (AllowedTools != null && AllowedTools.Any())
            {
                root["tools"] = JArray.FromObject(
                    AllowedTools.Select(t => new
                    {
                        type = "function",
                        function = new
                        {
                            name = t.Name,
                            description = t.Description,
                            parameters = t.ParametersSchema
                        }
                    })
                );

                root["tool_choice"] = ToolCallMode.ToString().ToLower();
            }
            return root.ToString(Formatting.Indented);
        }
        public override LLMRequestBase DeepClone()
        {
            return new LLMTextRequest(
                prompt: Prompt.Clone(),     // **only deep clone here**
                toolCallMode: ToolCallMode,
                allowedTools: AllowedTools, // allowed to share — immutable list
                model: Model,
                options: Options
            );
        }
    }
    public sealed class LLMStructuredRequest : LLMTextRequest
    {
        public Type ResultType { get; internal set; }
        public JObject? Schema { get; internal set; }

        public LLMStructuredRequest(
            Conversation prompt,
            Type resultType,
            IEnumerable<Tool>? allowedTools = null,
            ToolCallMode toolCallMode = ToolCallMode.Disabled,
            string? model = null,
            LLMGenerationOptions? options = null)
            : base(prompt, toolCallMode, allowedTools, model, options)
        {
            ResultType = resultType;
            Schema = null;
        }

        public override string ToSerializablePayload()
        {
            var root = new JObject
            {
                ["model"] = Model,
                ["messages"] = JArray.FromObject(Prompt.GetSerializableMessages())
            };

            if (Schema != null)
            {
                root["response_format"] = new JObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JObject
                    {
                        ["name"] = ResultType.Name,
                        ["strict"] = true,
                        ["schema"] = Schema
                    }
                };
            }

            if (AllowedTools != null && AllowedTools.Any())
            {
                root["tools"] = JArray.FromObject(
                    AllowedTools.Select(t => new
                    {
                        type = "function",
                        function = new
                        {
                            name = t.Name,
                            description = t.Description,
                            parameters = t.ParametersSchema
                        }
                    })
                );

                root["tool_choice"] = ToolCallMode.ToString().ToLower();
            }

            return root.ToString(Formatting.Indented);
        }

        public override LLMRequestBase DeepClone()
        {
            return new LLMStructuredRequest(
                prompt: Prompt.Clone(),
                resultType: ResultType,
                allowedTools: AllowedTools,
                toolCallMode: ToolCallMode,
                model: Model,
                options: Options
            )
            {
                Schema = Schema
            };
        }
    }

    public sealed class LLMInitOptions
    {
        public string? BaseUrl { get; set; } = null;
        public string? ApiKey { get; set; } = null;
        public string? Model { get; set; } = null;
    }

    public interface ILLMClient
    {
        Task<LLMTextResponse> ExecuteAsync(
            LLMTextRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null);

        Task<LLMStructuredResponse> ExecuteAsync(
            LLMStructuredRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null);
    }


    public enum ToolCallMode
    {
        None,     // expose tools but forbid calls
        Auto,     // allow text or tool calls
        Required,  // force tool call 
        Disabled,  // Don't send tools to LLM at all
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

        protected LLMResponseBase(
            string finishReason)
        {
            FinishReason = finishReason ?? "stop";
            TokenUsage = null;
        }
        public abstract string ToSerializablePayload();
    }

    public class LLMTextResponse : LLMResponseBase
    {
        public string? AssistantMessage { get; }
        public ToolCall? ToolCall { get; }

        public LLMTextResponse(
            string? assistantMessage,
            ToolCall? toolCall,
            string finishReason)
            : base(finishReason)
        {
            AssistantMessage = assistantMessage;
            ToolCall = toolCall;
        }
        public override string ToSerializablePayload()
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(AssistantMessage))
                sb.Append(AssistantMessage);

            if (ToolCall != null)
            {
                // The ID accounts for ~3-5 tokens
                if (!string.IsNullOrEmpty(ToolCall.Id))
                    sb.Append(ToolCall.Id);

                // The Name accounts for ~2-5 tokens
                if (!string.IsNullOrEmpty(ToolCall.Name))
                    sb.Append(ToolCall.Name);

                // The Arguments account for the rest
                if (ToolCall.Arguments != null)
                    sb.Append(ToolCall.Arguments.ToString(Formatting.Indented));
            }

            return sb.ToString();
        }
    }
    public sealed class LLMStructuredResponse : LLMTextResponse
    {
        public JToken RawJson { get; }
        public object? Result { get; }

        public LLMStructuredResponse(
            string? assistantMessage,
            ToolCall? toolCall,
            JToken rawJson,
            object? result,
            string finishReason)
            : base(assistantMessage, toolCall, finishReason)
        {
            RawJson = rawJson;
            Result = result;
        }

        public override string ToSerializablePayload()
        {
            return RawJson.ToString(Formatting.Indented);
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
