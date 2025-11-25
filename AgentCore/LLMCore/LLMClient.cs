using AgentCore.Chat;
using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLMCore
{
    public interface IChunkHandler
    {
        void PrepareRequest(LLMRequestBase request);
        void OnChunk(LLMStreamChunk chunk);
        object BuildResponse(string finishReason, int inputTokens, int outputTokens);
    }
    public abstract class LLMRequestBase
    {
        public Conversation Prompt { get; internal set; }
        public string Model { get; }
        public LLMGenerationOptions Options { get; }
        protected LLMRequestBase(
            Conversation prompt,
            string model = null,
            LLMGenerationOptions options = null)
        {
            Prompt = prompt;
            Model = model;
            Options = options;
        }

        public abstract LLMRequestBase DeepClone();
    }
    public sealed class LLMRequest : LLMRequestBase
    {
        public ToolCallMode ToolCallMode { get; }
        public IEnumerable<Tool> AllowedTools { get; internal set; }

        public LLMRequest(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            IEnumerable<Tool> allowedTools = null,
            string model = null,
            LLMGenerationOptions options = null)
            : base(prompt, model, options)
        {
            AllowedTools = allowedTools;
            ToolCallMode = toolCallMode;
        }

        public override LLMRequestBase DeepClone()
        {
            return new LLMRequest(
                prompt: Prompt.Clone(),     // **only deep clone here**
                toolCallMode: ToolCallMode,
                allowedTools: AllowedTools, // allowed to share — immutable list
                model: Model,
                options: Options
            );
        }
    }
    public sealed class LLMStructuredRequest : LLMRequestBase
    {
        public Type ResultType { get; internal set; }
        public JObject Schema { get; internal set; }

        public IEnumerable<Tool> AllowedTools { get; internal set; }
        public ToolCallMode ToolCallMode { get; }

        public LLMStructuredRequest(
            Conversation prompt,
            Type resultType,
            IEnumerable<Tool> allowedTools = null,
            ToolCallMode toolCallMode = ToolCallMode.Disabled,
            string model = null,
            LLMGenerationOptions options = null)
            : base(prompt, model, options)
        {
            ResultType = resultType;
            AllowedTools = allowedTools;
            ToolCallMode = toolCallMode;
            Schema = null;
        }

        public override LLMRequestBase DeepClone()
        {
            return new LLMStructuredRequest(
                prompt: Prompt.Clone(),        // **deep clone prompt only**
                resultType: ResultType,
                allowedTools: AllowedTools,    // immutable list shared
                toolCallMode: ToolCallMode,
                model: Model,
                options: Options
            )
            {
                Schema = Schema                 // Schema is immutable — share
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
        Task<LLMResponse> ExecuteAsync(
            LLMRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null);

        Task<LLMStructuredResponse<T>> ExecuteAsync<T>(
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
        public int InputTokens { get; }
        public int OutputTokens { get; }

        protected LLMResponseBase(
            string finishReason,
            int inputTokens,
            int outputTokens)
        {
            FinishReason = finishReason ?? "stop";
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
        }
    }

    public sealed class LLMResponse : LLMResponseBase
    {
        public string? AssistantMessage { get; }
        public ToolCall ToolCall { get; }

        public LLMResponse(
            string? assistantMessage,
            ToolCall toolCall,
            string finishReason,
            int input,
            int output)
            : base(finishReason, input, output)
        {
            AssistantMessage = assistantMessage;
            ToolCall = toolCall;
        }
    }

    public sealed class LLMStructuredResponse<T> : LLMResponseBase
    {
        public JToken RawJson { get; }
        public T Result { get; }

        public LLMStructuredResponse(
            JToken rawJson,
            T result,
            string finishReason,
            int inputTokens,
            int outputTokens)
            : base(finishReason, inputTokens, outputTokens)
        {
            RawJson = rawJson;
            Result = result;
        }
    }

    public enum StreamKind
    {
        Text,
        ToolCallDelta,
        ToolCall,
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
        public int? InputTokens { get; }
        public int? OutputTokens { get; }

        public LLMStreamChunk(
            StreamKind kind,
            object? payload = null,
            string? finish = null,
            int? input = null,
            int? output = null)
        {
            Kind = kind;
            Payload = payload;
            FinishReason = finish;
            InputTokens = input;
            OutputTokens = output;
        }
    }
    public static class LLMStreamChunkExtensions
    {
        public static string? AsText(this LLMStreamChunk chunk)
            => chunk.Payload as string;

        public static ToolCall? AsToolCall(this LLMStreamChunk chunk)
            => chunk.Payload as ToolCall;
        public static ToolCallDelta AsToolCallDelta(this LLMStreamChunk chunk)
            => chunk.Payload as ToolCallDelta;
    }
    public class ToolCallDelta
    {
        public string? Name { get; set; }
        public string? Delta { get; set; }
    }
}
