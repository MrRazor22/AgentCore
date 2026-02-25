using AgentCore.LLM;
using AgentCore.Tokens;

namespace AgentCore.Chat;

public interface IContentDelta { }

public sealed record TextDelta(string Value) : IContentDelta;

public sealed record ToolCallDelta(
    string? Id,
    string? Name,
    string? ArgumentsDelta
) : IContentDelta;

public sealed record MetaDelta(FinishReason FinishReason, TokenUsage? TokenUsage) : IContentDelta;
