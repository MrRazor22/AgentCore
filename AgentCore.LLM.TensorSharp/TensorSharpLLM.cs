using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using TensorSharp.Models;
using TensorSharp.Runtime;
using TensorSharp.Runtime.Scheduling;

namespace AgentCore.LLM.TensorSharp;

/// <summary>
/// Adapts an in-process TensorSharp InferenceEngine to the AgentCore ILLM event-streaming interface.
/// Supports zero-serialization streaming for text, reasoning tags, tool calls, and paged KV cache reuse.
/// </summary>
public sealed class TensorSharpLLM(
    InferenceEngine engine,
    IModelArchitecture model,
    SamplingConfig? defaultSamplingConfig = null) : ILLM
{
    private readonly InferenceEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IModelArchitecture _model = model ?? throw new ArgumentNullException(nameof(model));
    private readonly SamplingConfig _defaultSampling = defaultSamplingConfig ?? SamplingConfig.Default;
    private readonly GgufPromptRenderer _renderer = new();

    public async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages == null || messages.Count == 0)
            yield break;

        // 1. Format messages to TensorSharp chat messages
        var chatMessages = messages.Select(ToChatMessage).ToList();
        var toolFunctions = tools != null && tools.Count > 0 ? ToToolFunctions(tools) : null;

        string template = _model.Config?.ChatTemplate ?? string.Empty;
        string prompt = _renderer.Render(
            template: template,
            messages: chatMessages,
            addGenerationPrompt: true,
            architecture: _model.Config?.Architecture,
            tools: toolFunctions,
            enableThinking: true);

        List<int> promptTokens = _model.Tokenizer.Encode(prompt);
        if (promptTokens.Count == 0)
            yield break;

        int maxNewTokens = _defaultSampling.MaxTokens > 0 ? _defaultSampling.MaxTokens : 4096;
        var seq = new SequenceState(
            requestId: $"req-{Guid.NewGuid():N}",
            promptTokens: promptTokens,
            maxNewTokens: maxNewTokens,
            blockSize: _engine.PoolStats.blockSize > 0 ? _engine.PoolStats.blockSize : 16,
            samplingConfig: _defaultSampling);

        var handle = _engine.SubmitRequest(seq, ct);

        // 2. Stream tokens and parse reasoning, tool calls, and text
        yield return new MessageStart(Role.Assistant, Id: seq.RequestId, Model: _model.Config?.Architecture ?? "local");

        int nextBlockIndex = 0;
        bool inThinking = false;
        bool inToolCall = false;
        int activeBlockId = 0;
        int outTokens = 0;
        var toolBuffer = new System.Text.StringBuilder();

        await foreach (int tokenId in handle.Tokens.ReadAllAsync(ct).ConfigureAwait(false))
        {
            outTokens++;
            string piece = _model.Tokenizer.Decode([tokenId]);
            if (string.IsNullOrEmpty(piece))
                continue;

            // Handle thinking blocks (<think> ... </think>)
            if (piece.Contains("<think>", StringComparison.OrdinalIgnoreCase))
            {
                inThinking = true;
                activeBlockId = nextBlockIndex++;
                yield return new ReasoningStart(activeBlockId);
                piece = piece.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (inThinking)
            {
                if (piece.Contains("</think>", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = piece.Split(["</think>"], 2, StringSplitOptions.None);
                    if (!string.IsNullOrEmpty(parts[0]))
                        yield return new ReasoningDelta(activeBlockId, parts[0]);

                    yield return new ReasoningEnd(activeBlockId);
                    inThinking = false;
                    piece = parts.Length > 1 ? parts[1] : string.Empty;
                }
                else
                {
                    if (!string.IsNullOrEmpty(piece))
                        yield return new ReasoningDelta(activeBlockId, piece);
                    continue;
                }
            }

            if (string.IsNullOrEmpty(piece))
                continue;

            // Handle tool call detection (e.g. ```json or tool markers)
            if (!inToolCall && (piece.Contains("```json") || piece.Contains("{\"name\":") || piece.Contains("<tool_call>")))
            {
                inToolCall = true;
                activeBlockId = nextBlockIndex++;
                yield return new ToolCallStart(activeBlockId, $"call_{activeBlockId}", "function_call");
            }

            if (inToolCall)
            {
                toolBuffer.Append(piece);
                yield return new ToolCallDelta(activeBlockId, piece);

                if (piece.Contains("</tool_call>") || (toolBuffer.ToString().TrimEnd().EndsWith("}") && !piece.EndsWith(",")))
                {
                    yield return new ToolCallEnd(activeBlockId);
                    inToolCall = false;
                    toolBuffer.Clear();
                }
                continue;
            }

            // Normal text block
            if (activeBlockId == 0 && nextBlockIndex == 0)
            {
                activeBlockId = nextBlockIndex++;
                yield return new TextStart(activeBlockId);
            }

            yield return new TextDelta(activeBlockId, piece);
        }

        if (inThinking)
            yield return new ReasoningEnd(activeBlockId);

        if (inToolCall)
            yield return new ToolCallEnd(activeBlockId);
        else if (nextBlockIndex > 0)
            yield return new TextEnd(activeBlockId);

        yield return new MessageEnd(
            FinishReason: "stop",
            Usage: new TokenUsage(InputTokens: promptTokens.Count, OutputTokens: outTokens));
    }

    private static ChatMessage ToChatMessage(Message msg)
    {
        string text = string.Join("\n", msg.Contents.Select(c => c switch
        {
            Text t => t.Value,
            _ => c.ToString() ?? string.Empty
        }));

        return new ChatMessage
        {
            Role = msg.Role switch
            {
                Role.System => "system",
                Role.User => "user",
                Role.Assistant => "assistant",
                Role.Tool => "tool",
                _ => "user"
            },
            Content = text
        };
    }

    private static List<ToolFunction> ToToolFunctions(IReadOnlyList<ToolDefinition> tools)
    {
        var list = new List<ToolFunction>();
        foreach (var def in tools)
        {
            var fn = new ToolFunction
            {
                Name = def.Name,
                Description = def.Description
            };

            if (def.ParametersSchema != null)
            {
                var element = def.ParametersSchema.ToJsonElement();
                if (element.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in props.EnumerateObject())
                    {
                        var p = new ToolParameter();
                        if (prop.Value.TryGetProperty("type", out var typeProp))
                            p.Type = typeProp.GetString() ?? "string";
                        if (prop.Value.TryGetProperty("description", out var descProp))
                            p.Description = descProp.GetString() ?? string.Empty;
                        fn.Parameters[prop.Name] = p;
                    }
                }
                if (element.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in req.EnumerateArray())
                    {
                        if (item.GetString() is { } reqName)
                            fn.Required.Add(reqName);
                    }
                }
            }
            list.Add(fn);
        }
        return list;
    }
}
