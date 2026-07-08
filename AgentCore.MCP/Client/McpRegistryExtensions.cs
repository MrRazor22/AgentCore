using AgentCore.Tooling;
using ModelContextProtocol.Client;

namespace AgentCore.MCP.Client;

/// <summary>
/// Extension to bridge MCP tools (McpClientTool) into the AgentCore IToolRegistry.
/// </summary>
public static class McpRegistryExtensions
{
    public static void RegisterMcpTools(
        this IToolRegistry registry,
        string serverName,
        IEnumerable<McpClientTool> mcpTools)
    {
        foreach (var mcpTool in mcpTools)
        {
            registry.Register(new McpTool(serverName, mcpTool));
        }
    }
}
