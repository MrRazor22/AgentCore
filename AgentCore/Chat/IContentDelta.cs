namespace AgentCore.Chat;

public interface IContentDelta { }

public sealed record TextDelta(string Value) : IContentDelta;

public sealed record ToolCallDelta(
    string? Id,
    string? Name,
    string? ArgumentsDelta
) : IContentDelta;
