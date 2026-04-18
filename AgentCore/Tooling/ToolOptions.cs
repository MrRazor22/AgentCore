using System;

namespace AgentCore.Tooling;

public enum ToolProfile
{
    Messaging,
    Full,
    Custom
}

public sealed record ToolOptions
{
    public int MaxConcurrency { get; init; } = 5;
    public TimeSpan? DefaultTimeout { get; init; } = null;

    /// <summary>
    /// Auto-approve tools in these categories (Cline-style)
    /// </summary>
    public string[] AutoApproveCategories { get; init; } = [];

    /// <summary>
    /// Tool profile for approval decisions (OpenClaw-style)
    /// </summary>
    public ToolProfile ToolProfile { get; init; } = ToolProfile.Full;

    /// <summary>
    /// Explicitly allowed tool names (OpenClaw-style)
    /// </summary>
    public string[] Allow { get; init; } = [];

    /// <summary>
    /// Explicitly denied tool names (OpenClaw-style)
    /// </summary>
    public string[] Deny { get; init; } = [];

    /// <summary>
    /// YOLO mode - auto-execute all tools without approval (Cline-style)
    /// </summary>
    public bool YoloMode { get; init; } = false;
}
