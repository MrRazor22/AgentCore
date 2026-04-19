namespace AgentCore.Memory;

/// <summary>
/// MCP-compliant skill representation for memory engine integration.
/// </summary>
public sealed record McpSkill(string Name, string Description, string Content, double Confidence, string StepsJson = "[]")
{
    public static McpSkill? FromMemoryEntry(MemoryEntry? entry)
    {
        if (entry == null) return null;
        return new McpSkill(
            entry.Name ?? "unnamed",
            entry.Kind.ToString(),
            entry.Content ?? "",
            entry.Confidence,
            "[]");
    }
}
