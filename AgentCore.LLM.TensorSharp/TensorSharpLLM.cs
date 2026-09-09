using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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
    private const string ThinkStart = "<think>";
    private const string ThinkEnd = "</think>";
    private const string ToolStart = "<tool_call>";
    private const string ToolEnd = "</tool_call>";

    private readonly InferenceEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IModelArchitecture _model = model ?? throw new ArgumentNullException(nameof(model));
    private readonly SamplingConfig _defaultSampling = defaultSamplingConfig ?? SamplingConfig.Default;
    private readonly GgufPromptRenderer _renderer = new();

    private enum BlockType { None, Text, Reasoning, Tool }

    public async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages == null || messages.Count == 0)
            yield break;

        string prompt = _renderer.Render(
            template: _model.Config?.ChatTemplate ?? string.Empty,
            messages: messages.Select(m => m.ToTensorSharpMessage()).ToList(),
            addGenerationPrompt: true,
            architecture: _model.Config?.Architecture,
            tools: tools != null && tools.Count > 0 ? tools.ToTensorSharpTools() : null,
            enableThinking: true);

        List<int> promptTokens = _model.Tokenizer.Encode(prompt);
        if (promptTokens.Count == 0)
            yield break;

        var seq = new SequenceState(
            requestId: $"req-{Guid.NewGuid():N}",
            promptTokens: promptTokens,
            maxNewTokens: _defaultSampling.MaxTokens > 0 ? _defaultSampling.MaxTokens : 4096,
            blockSize: _engine.PoolStats.blockSize > 0 ? _engine.PoolStats.blockSize : 16,
            samplingConfig: _defaultSampling);

        var handle = _engine.SubmitRequest(seq, ct);
        yield return new MessageStart(Role.Assistant, Id: seq.RequestId, Model: _model.Config?.Architecture ?? "local");

        int nextBlockId = 0, currentId = 0, outTokens = 0;
        var currentType = BlockType.None;

        IEnumerable<IMessageEvent> SwitchBlock(BlockType newType)
        {
            if (currentType == newType) yield break;
            if (currentType == BlockType.Text) yield return new TextEnd(currentId);
            else if (currentType == BlockType.Reasoning) yield return new ReasoningEnd(currentId);
            else if (currentType == BlockType.Tool) yield return new ToolCallEnd(currentId);

            currentType = newType;
            currentId = nextBlockId++;

            if (newType == BlockType.Text) yield return new TextStart(currentId);
            else if (newType == BlockType.Reasoning) yield return new ReasoningStart(currentId);
            else if (newType == BlockType.Tool) yield return new ToolCallStart(currentId, $"call_{Guid.NewGuid():N}", "tool");
        }

        await foreach (int tokenId in handle.Tokens.ReadAllAsync(ct).ConfigureAwait(false))
        {
            outTokens++;
            string piece = _model.Tokenizer.Decode([tokenId]);
            if (string.IsNullOrEmpty(piece))
                continue;

            if (piece.Contains(ThinkStart, StringComparison.OrdinalIgnoreCase)) { foreach (var e in SwitchBlock(BlockType.Reasoning)) yield return e; piece = piece.Replace(ThinkStart, "", StringComparison.OrdinalIgnoreCase); }
            if (piece.Contains(ThinkEnd, StringComparison.OrdinalIgnoreCase)) { foreach (var e in SwitchBlock(BlockType.Text)) yield return e; piece = piece.Replace(ThinkEnd, "", StringComparison.OrdinalIgnoreCase); }
            if (piece.Contains(ToolStart, StringComparison.OrdinalIgnoreCase)) { foreach (var e in SwitchBlock(BlockType.Tool)) yield return e; piece = piece.Replace(ToolStart, "", StringComparison.OrdinalIgnoreCase); }
            if (piece.Contains(ToolEnd, StringComparison.OrdinalIgnoreCase)) { foreach (var e in SwitchBlock(BlockType.Text)) yield return e; piece = piece.Replace(ToolEnd, "", StringComparison.OrdinalIgnoreCase); }

            if (string.IsNullOrEmpty(piece))
                continue;

            if (currentType == BlockType.None)
                foreach (var e in SwitchBlock(BlockType.Text)) yield return e;

            if (currentType == BlockType.Reasoning)
                yield return new ReasoningDelta(currentId, piece);
            else if (currentType == BlockType.Tool)
                yield return new ToolCallDelta(currentId, piece);
            else
                yield return new TextDelta(currentId, piece);
        }

        foreach (var e in SwitchBlock(BlockType.None)) yield return e;
        yield return new MessageEnd(
            FinishReason: "stop",
            Usage: new TokenUsage(InputTokens: promptTokens.Count, OutputTokens: outTokens));
    }
}
