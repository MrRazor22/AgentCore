using AgentCore;
using AgentCore.Tool;
using ModelContextProtocol.Client;

namespace AgentCore.MCP;

public static class McpToolExtensions
{ 
    public static async Task<Tooling> AddMcpToolsAsync(this Tooling tooling, McpClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var mcpTools = tools.Select(t => (ITool)new McpTool(client, t.ProtocolTool));
        return tooling.With([.. tooling.Tools, .. mcpTools]);
    }
} 