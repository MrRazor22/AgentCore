namespace CodeSharp.UI;

public sealed record ToolDisplayInfo(
    string DisplayName,
    string ArgSummary,
    string? LongDetails = null
);
