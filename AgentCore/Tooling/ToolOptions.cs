using System;

namespace AgentCore.Tooling;

public sealed record ToolOptions
{
    public int MaxConcurrency { get; init; } = 5;
    public TimeSpan? DefaultTimeout { get; init; } = null;
    public int MaxResultLength { get; init; } = 32768;
}
