using AgentCore.LLM;
using AgentCore.Tokens;

namespace AgentCore.Conversation;

public interface IContentDelta { }

public sealed record TextDelta(string Value) : IContentDelta;

public sealed record ReasoningDelta(string Value) : IContentDelta;

public sealed record ToolCallDelta(
    int Index,
    string? Id,
    string? Name,
    string? ArgumentsDelta
) : IContentDelta;

public sealed record MetaDelta(FinishReason FinishReason, TokenUsage? TokenUsage) : IContentDelta;
