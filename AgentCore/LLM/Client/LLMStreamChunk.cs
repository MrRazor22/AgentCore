using AgentCore.Chat;
using AgentCore.Tokens;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentCore.LLM.Client
{
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
        public FinishReason? FinishReason { get; }

        public LLMStreamChunk(
            StreamKind kind,
            object? payload = null,
            FinishReason? finish = null)
        {
            Kind = kind;
            Payload = payload;
            FinishReason = finish;
        }
    }
    public static class LLMStreamChunkExtensions
    {
        public static FinishReason? AsFinishReason(this LLMStreamChunk chunk)
             => chunk.Payload as FinishReason?;
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
